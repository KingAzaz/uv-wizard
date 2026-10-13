#nullable enable

using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace UVWizard.Core.Meshes
{
    /// <summary>
    /// Writes merged triangles' atlas UVs into UV0 of a copy of their mesh, and leaves everything
    /// else on it as it was.
    /// </summary>
    internal static class Uv0Rewriter
    {
        /// <summary>
        /// Returns a copy of the mesh whose merged triangles sample the atlas. A vertex whose
        /// triangles want different UV0s, because they sit in different charts or some belong to an
        /// excluded material, gets one copy per UV0 with every other attribute copied. The merged
        /// submeshes collapse into one.
        /// </summary>
        /// <param name="atlasUvs">
        /// One entry per submesh. Null keeps the submesh's UV0, for an excluded material. Otherwise
        /// one atlas UV per triangle corner, in the order of the submesh's indices.
        /// </param>
        public static RewrittenMesh Rewrite(Mesh source, IReadOnlyList<Vector2[]?> atlasUvs)
        {
            // The editor's MeshUtility variant reads meshes whose Read/Write is off.
            using var meshes = MeshUtility.AcquireReadOnlyMeshData(source);
            var data = meshes[0];
            if (atlasUvs.Count != data.subMeshCount)
                throw new ArgumentException($"{atlasUvs.Count} entries for {data.subMeshCount} submeshes.", nameof(atlasUvs));

            var indices = new int[data.subMeshCount][];
            for (var s = 0; s < data.subMeshCount; s++)
            {
                using var submeshIndices = new NativeArray<int>(data.GetSubMesh(s).indexCount, Allocator.Temp);
                data.GetIndices(submeshIndices, s);
                indices[s] = submeshIndices.ToArray();
                if (atlasUvs[s] is not { } uvs) continue;
                if (!data.HasVertexAttribute(VertexAttribute.TexCoord0))
                    throw new ArgumentException("A mesh without UV0 can't be merged.", nameof(atlasUvs));
                if (data.GetSubMesh(s).topology != MeshTopology.Triangles)
                    throw new ArgumentException($"Submesh {s} isn't made of triangles, so it can't be merged.", nameof(atlasUvs));
                if (uvs.Length != indices[s].Length)
                    throw new ArgumentException($"Submesh {s} has {indices[s].Length} corners but {uvs.Length} atlas UVs.", nameof(atlasUvs));
            }

            var copies = new VertexCopies(data.vertexCount);
            var corners = new int[data.subMeshCount][];
            for (var s = 0; s < data.subMeshCount; s++)
            {
                corners[s] = new int[indices[s].Length];
                for (var k = 0; k < indices[s].Length; k++)
                    corners[s][k] = copies.Want(indices[s][k], atlasUvs[s]?[k]);
            }
            var (sourceOf, uv0Of, firstCopy) = copies.Assign();
            for (var s = 0; s < data.subMeshCount; s++)
            for (var k = 0; k < corners[s].Length; k++)
                corners[s][k] += firstCopy[indices[s][k]];

            Mesh mesh;
            if (sourceOf.Length == data.vertexCount)
            {
                // Nothing splits, so a native copy keeps every attribute and blend shape, and only
                // UV0 changes. Rebuilding blend shapes frame by frame slows down with each frame
                // added: about 5 s for a 65k-vertex body with 695 frames, against 0.2 s for this.
                mesh = UnityEngine.Object.Instantiate(source);
                mesh.name = source.name;
                if (data.HasVertexAttribute(VertexAttribute.TexCoord0))
                {
                    var stream = data.GetVertexAttributeStream(VertexAttribute.TexCoord0);
                    var bytes = CopiedStream(data, stream, sourceOf, uv0Of);
                    mesh.SetVertexBufferData(bytes, 0, 0, bytes.Length, stream);
                }
            }
            else
            {
                mesh = new Mesh { name = source.name };
                CopyVertexStreams(data, mesh, sourceOf, uv0Of);
                CopyBoneWeights(source, mesh, sourceOf);
                CopyBlendShapes(source, mesh, sourceOf);
            }
            var (submeshOf, merged) = WriteSubmeshes(data, mesh, atlasUvs, corners);
            mesh.bounds = source.bounds;
            return new RewrittenMesh(mesh, submeshOf, merged);
        }

        // Every vertex stream, byte for byte, into a new mesh with the source's layout.
        static void CopyVertexStreams(Mesh.MeshData data, Mesh mesh, int[] sourceOf, Vector2?[] uv0Of)
        {
            var attributes = new List<VertexAttributeDescriptor>();
            foreach (VertexAttribute attribute in Enum.GetValues(typeof(VertexAttribute)))
                if (data.HasVertexAttribute(attribute))
                    attributes.Add(new VertexAttributeDescriptor(attribute, data.GetVertexAttributeFormat(attribute),
                        data.GetVertexAttributeDimension(attribute), data.GetVertexAttributeStream(attribute)));
            mesh.SetVertexBufferParams(sourceOf.Length, attributes.ToArray());

            for (var stream = 0; stream < data.vertexBufferCount; stream++)
            {
                if (mesh.GetVertexBufferStride(stream) != data.GetVertexBufferStride(stream))
                    throw new InvalidOperationException($"Vertex stream {stream} changed layout in the copy.");
                var copied = CopiedStream(data, stream, sourceOf, uv0Of);
                mesh.SetVertexBufferData(copied, 0, 0, copied.Length, stream);
            }
        }

        // One vertex stream's bytes for the copies, with UV0 written in its own format where a copy wants an atlas UV.
        static byte[] CopiedStream(Mesh.MeshData data, int stream, int[] sourceOf, Vector2?[] uv0Of)
        {
            var stride = data.GetVertexBufferStride(stream);
            var bytes = data.GetVertexData<byte>(stream).ToArray();
            var copied = new byte[sourceOf.Length * stride];
            var hasUv = data.GetVertexAttributeStream(VertexAttribute.TexCoord0) == stream;
            var uvOffset = data.GetVertexAttributeOffset(VertexAttribute.TexCoord0);
            var uvFormat = data.GetVertexAttributeFormat(VertexAttribute.TexCoord0);
            for (var v = 0; v < sourceOf.Length; v++)
            {
                Buffer.BlockCopy(bytes, sourceOf[v] * stride, copied, v * stride, stride);
                if (hasUv && uv0Of[v] is { } uv) WriteUv(copied.AsSpan(v * stride + uvOffset), uv, uvFormat);
            }
            return copied;
        }

        static void WriteUv(Span<byte> at, Vector2 uv, VertexAttributeFormat format)
        {
            switch (format)
            {
                case VertexAttributeFormat.Float32:
                    BitConverter.TryWriteBytes(at, uv.x);
                    BitConverter.TryWriteBytes(at.Slice(4), uv.y);
                    break;
                case VertexAttributeFormat.Float16:
                    BitConverter.TryWriteBytes(at, Mathf.FloatToHalf(uv.x));
                    BitConverter.TryWriteBytes(at.Slice(2), Mathf.FloatToHalf(uv.y));
                    break;
                default:
                    throw new NotSupportedException($"UV0 stored as {format} can't hold atlas UVs.");
            }
        }

        // Through the bone-weight API rather than the vertex streams, which hold at most four bones per vertex.
        static void CopyBoneWeights(Mesh source, Mesh mesh, int[] sourceOf)
        {
            var bonesPerVertex = source.GetBonesPerVertex();
            if (bonesPerVertex.Length == 0) return;
            var weights = source.GetAllBoneWeights();
            var start = new int[bonesPerVertex.Length];
            for (var v = 1; v < start.Length; v++) start[v] = start[v - 1] + bonesPerVertex[v - 1];

            var copiedPerVertex = new byte[sourceOf.Length];
            var copiedWeights = new List<BoneWeight1>();
            for (var v = 0; v < sourceOf.Length; v++)
            {
                var s = sourceOf[v];
                copiedPerVertex[v] = bonesPerVertex[s];
                for (var b = 0; b < bonesPerVertex[s]; b++) copiedWeights.Add(weights[start[s] + b]);
            }
            using var perVertexArray = new NativeArray<byte>(copiedPerVertex, Allocator.Temp);
            using var weightArray = new NativeArray<BoneWeight1>(copiedWeights.ToArray(), Allocator.Temp);
            mesh.SetBoneWeights(perVertexArray, weightArray);
            mesh.bindposes = source.bindposes;
        }

        static void CopyBlendShapes(Mesh source, Mesh mesh, int[] sourceOf)
        {
            var dv = new Vector3[source.vertexCount];
            var dn = new Vector3[source.vertexCount];
            var dt = new Vector3[source.vertexCount];
            var copiedDv = new Vector3[sourceOf.Length];
            var copiedDn = new Vector3[sourceOf.Length];
            var copiedDt = new Vector3[sourceOf.Length];
            var runs = Runs(sourceOf);
            for (var shape = 0; shape < source.blendShapeCount; shape++)
            for (var frame = 0; frame < source.GetBlendShapeFrameCount(shape); frame++)
            {
                source.GetBlendShapeFrameVertices(shape, frame, dv, dn, dt);
                foreach (var (copy, from, length) in runs)
                {
                    Array.Copy(dv, from, copiedDv, copy, length);
                    Array.Copy(dn, from, copiedDn, copy, length);
                    Array.Copy(dt, from, copiedDt, copy, length);
                }
                mesh.AddBlendShapeFrame(source.GetBlendShapeName(shape), source.GetBlendShapeFrameWeight(shape, frame),
                    copiedDv, copiedDn, copiedDt);
            }
        }

        // Stretches of copies whose source vertices are consecutive, as (first copy, first source
        // vertex, length). Few vertices split, so a large mesh has few stretches, and copying them
        // whole is much faster than copying vertex by vertex across hundreds of blend-shape frames.
        static List<(int Copy, int From, int Length)> Runs(int[] sourceOf)
        {
            var runs = new List<(int, int, int)>();
            var start = 0;
            for (var v = 1; v <= sourceOf.Length; v++)
            {
                if (v < sourceOf.Length && sourceOf[v] == sourceOf[v - 1] + 1) continue;
                runs.Add((start, sourceOf[start], v - start));
                start = v;
            }
            return runs;
        }

        // Excluded submeshes keep their order; the merged ones become one, where the first of them was.
        static (int[] SubmeshOf, int Merged) WriteSubmeshes(Mesh.MeshData data, Mesh mesh,
            IReadOnlyList<Vector2[]?> atlasUvs, int[][] corners)
        {
            var submeshOf = new int[data.subMeshCount];
            var written = new List<(List<int> Indices, MeshTopology Topology)>();
            var merged = -1;
            for (var s = 0; s < data.subMeshCount; s++)
            {
                if (atlasUvs[s] == null)
                {
                    submeshOf[s] = written.Count;
                    written.Add((new List<int>(corners[s]), data.GetSubMesh(s).topology));
                    continue;
                }
                if (merged < 0)
                {
                    merged = written.Count;
                    written.Add((new List<int>(), MeshTopology.Triangles));
                }
                submeshOf[s] = merged;
                written[merged].Indices.AddRange(corners[s]);
            }

            var all = new List<int>();
            foreach (var (submeshIndices, _) in written) all.AddRange(submeshIndices);
            var format = mesh.vertexCount > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetIndexBufferParams(all.Count, format);
            if (format == IndexFormat.UInt32) mesh.SetIndexBufferData(all, 0, 0, all.Count);
            else mesh.SetIndexBufferData(all.ConvertAll(i => (ushort)i), 0, 0, all.Count);
            // All at once: setting subMeshCount alone on a copied mesh truncates the new indices.
            var descriptors = new SubMeshDescriptor[written.Count];
            var start = 0;
            for (var s = 0; s < written.Count; s++)
            {
                descriptors[s] = new SubMeshDescriptor(start, written[s].Indices.Count, written[s].Topology);
                start += written[s].Indices.Count;
            }
            mesh.SetSubMeshes(descriptors);
            return (submeshOf, merged);
        }

        /// <summary>
        /// The copies each source vertex needs: one per distinct UV0 its corners want, in the order
        /// they're first wanted, or one keeping its UV0 when no corner uses it.
        /// </summary>
        sealed class VertexCopies
        {
            readonly List<Vector2?>?[] wanted;

            public VertexCopies(int vertexCount) => wanted = new List<Vector2?>?[vertexCount];

            /// <summary>
            /// Records that a corner of the vertex wants this UV0 (null: its own), and returns which
            /// of the vertex's copies it uses.
            /// </summary>
            public int Want(int vertex, Vector2? uv0)
            {
                var copies = wanted[vertex] ??= new List<Vector2?>(1);
                var copy = copies.IndexOf(uv0);
                if (copy >= 0) return copy;
                copies.Add(uv0);
                return copies.Count - 1;
            }

            /// <summary>
            /// Lays the copies out in source vertex order. Returns each copy's source vertex and
            /// UV0 (null: its own), and each source vertex's first copy.
            /// </summary>
            public (int[] SourceOf, Vector2?[] Uv0Of, int[] FirstCopy) Assign()
            {
                var firstCopy = new int[wanted.Length];
                var count = 0;
                for (var v = 0; v < wanted.Length; v++)
                {
                    firstCopy[v] = count;
                    count += wanted[v]?.Count ?? 1;
                }
                var sourceOf = new int[count];
                var uv0Of = new Vector2?[count];
                for (var v = 0; v < wanted.Length; v++)
                {
                    var copies = wanted[v];
                    for (var c = 0; c < (copies?.Count ?? 1); c++)
                    {
                        sourceOf[firstCopy[v] + c] = v;
                        uv0Of[firstCopy[v] + c] = copies?[c];
                    }
                }
                return (sourceOf, uv0Of, firstCopy);
            }
        }
    }

    /// <summary>A mesh rewritten to sample the atlas, and where its source submeshes went.</summary>
    internal sealed class RewrittenMesh
    {
        public RewrittenMesh(Mesh mesh, int[] submeshOf, int mergedSubmesh)
        {
            Mesh = mesh;
            SubmeshOf = submeshOf;
            MergedSubmesh = mergedSubmesh;
        }

        public Mesh Mesh { get; }

        /// <summary>For each source submesh, the rewritten mesh's submesh that holds its triangles.</summary>
        public IReadOnlyList<int> SubmeshOf { get; }

        /// <summary>The submesh that takes the merged material, or -1 when nothing on the mesh was merged.</summary>
        public int MergedSubmesh { get; }
    }
}
