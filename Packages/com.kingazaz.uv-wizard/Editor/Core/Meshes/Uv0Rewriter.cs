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
        /// submeshes collapse into one, unless <paramref name="needs"/> keeps them apart.
        /// </summary>
        /// <param name="atlasUvs">
        /// One entry per submesh. Null keeps the submesh's UV0, for an excluded material. Otherwise
        /// one atlas UV per triangle corner, in the order of the submesh's indices. A vertex splits
        /// wherever its corners' UVs differ at all, so the corners of a vertex in one chart must
        /// get bit-identical atlas UVs.
        /// </param>
        /// <param name="needs">
        /// What a tool that runs after the bake needs kept: the source UV0 in a channel the mesh doesn't use,
        /// and the merged submeshes in their own places. Never one from <see cref="LaterToolNeeds.KeepsOut"/>, such as
        /// <see cref="LaterToolNeeds.CantKeepSourceUv0"/>, whose renderer stays out of the bake.
        /// </param>
        /// <remarks>
        /// UV0 keeps its format: Float32, Float16 or UNorm16. Float16 holds atlas UVs to within
        /// 2^-11, a texel of a 2048 atlas.
        /// </remarks>
        public static RewrittenMesh Rewrite(Mesh source, IReadOnlyList<Vector2[]?> atlasUvs,
            LaterToolNeeds needs = default)
        {
            // The editor's MeshUtility variant reads meshes whose Read/Write is off.
            using var meshes = MeshUtility.AcquireReadOnlyMeshData(source);
            var data = meshes[0];
            if (atlasUvs.Count != data.subMeshCount)
                throw new ArgumentException($"{atlasUvs.Count} entries for {data.subMeshCount} submeshes.", nameof(atlasUvs));
            if (needs.KeptOutBecause != null)
                throw new ArgumentException("The renderer stays out of the bake, so its mesh can't be rewritten.", nameof(needs));
            if (needs.KeepSourceUv0In is { } keptIn)
            {
                if (keptIn is < 1 or > 7)
                    throw new ArgumentOutOfRangeException(nameof(needs), keptIn,
                        $"{nameof(LaterToolNeeds.KeepSourceUv0In)} must be a UV channel from 1 to 7.");
                if (data.HasVertexAttribute(TexCoord(keptIn)))
                    throw new ArgumentException($"The mesh already uses UV{keptIn}.", nameof(needs));
            }

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
                var format = data.GetVertexAttributeFormat(VertexAttribute.TexCoord0);
                if (!CanHoldAtlasUvs(format, data.GetVertexAttributeDimension(VertexAttribute.TexCoord0)))
                    throw new NotSupportedException(
                        $"UV0 stored as {format} x{data.GetVertexAttributeDimension(VertexAttribute.TexCoord0)} can't hold atlas UVs.");
            }

            var copies = new VertexCopies(data.vertexCount);
            var corners = new int[data.subMeshCount][];
            for (var s = 0; s < data.subMeshCount; s++)
            {
                corners[s] = new int[indices[s].Length];
                for (var k = 0; k < indices[s].Length; k++)
                    corners[s][k] = copies.Want(indices[s][k], atlasUvs[s]?[k]);
            }
            var (sourceOf, uv0Of) = copies.Assign();
            for (var s = 0; s < data.subMeshCount; s++)
            for (var k = 0; k < corners[s].Length; k++)
                corners[s][k] = copies.IndexOf(indices[s][k], corners[s][k]);

            Mesh mesh;
            RewritePath path;
            if (sourceOf.Length == data.vertexCount)
            {
                // Nothing splits, so a native copy keeps every attribute and blend shape, and only
                // UV0 changes. Rebuilding blend shapes frame by frame slows down with each frame
                // added: a 65k-vertex body with 695 frames takes about 5 s, more as copies are
                // added, against 0.1 s for this.
                path = RewritePath.Copied;
                mesh = UnityEngine.Object.Instantiate(source);
                mesh.name = source.name;
                if (data.HasVertexAttribute(VertexAttribute.TexCoord0))
                {
                    var stream = data.GetVertexAttributeStream(VertexAttribute.TexCoord0);
                    var bytes = CopiedStream(data, stream, sourceOf, uv0Of);
                    mesh.SetVertexBufferData(bytes, 0, 0, bytes.Length, stream);
                }
            }
            else if (!AnyMoves(source, sourceOf, data.vertexCount))
            {
                // The extra copies go after the source's vertices, so every blend-shape delta stays
                // on its vertex, and the copies get none, as their sources have none.
                path = RewritePath.Appended;
                mesh = UnityEngine.Object.Instantiate(source);
                mesh.name = source.name;
                CopyVertexStreams(data, mesh, sourceOf, uv0Of);
                CopyBoneWeights(data, source, mesh, sourceOf);
            }
            else
            {
                // A copy rather than a new mesh, so it keeps the source's UV0 and UV1 distribution
                // metrics, which Unity has no setter for.
                path = RewritePath.Rebuilt;
                mesh = UnityEngine.Object.Instantiate(source);
                mesh.name = source.name;
                mesh.ClearBlendShapes();
                CopyVertexStreams(data, mesh, sourceOf, uv0Of);
                CopyBoneWeights(data, source, mesh, sourceOf);
                CopyBlendShapes(source, mesh, sourceOf);
            }
            if (needs.KeepSourceUv0In is { } channel)
            {
                using var sourceUv0 = new NativeArray<Vector2>(data.vertexCount, Allocator.Temp);
                data.GetUVs(0, sourceUv0);
                var kept = new Vector2[sourceOf.Length];
                for (var v = 0; v < kept.Length; v++) kept[v] = sourceUv0[sourceOf[v]];
                mesh.SetUVs(channel, kept);
            }
            var (submeshOf, merged) = WriteSubmeshes(data, mesh, atlasUvs, corners, needs.KeepSubmeshes);
            mesh.bounds = source.bounds;
            // Mip streaming picks the atlas's mip level from UV0's texel density. Unity's
            // recalculation logs an error on a mesh with line or point submeshes, so those keep the
            // source's metric, as UV1 does.
            if (merged != null && AllTriangles(mesh)) mesh.RecalculateUVDistributionMetric(0);
            return new RewrittenMesh(mesh, submeshOf, merged, path);
        }

        static VertexAttribute TexCoord(int channel) => VertexAttribute.TexCoord0 + channel;

        /// <summary>True when UV0 stored this way can be rewritten to atlas positions.</summary>
        public static bool CanHoldAtlasUvs(VertexAttributeFormat format, int dimension) =>
            dimension >= 2 && TryWriteUv(stackalloc byte[8], Vector2.zero, format);

        static bool AllTriangles(Mesh mesh)
        {
            for (var s = 0; s < mesh.subMeshCount; s++)
                if (mesh.GetTopology(s) != MeshTopology.Triangles) return false;
            return true;
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
                if (hasUv && uv0Of[v] is { } uv && !TryWriteUv(copied.AsSpan(v * stride + uvOffset), uv, uvFormat))
                    throw new NotSupportedException($"UV0 stored as {uvFormat} can't hold atlas UVs.");
            }
            return copied;
        }

        // Writes the UV in the given format, or returns false for a format that can't hold atlas UVs.
        static bool TryWriteUv(Span<byte> at, Vector2 uv, VertexAttributeFormat format)
        {
            switch (format)
            {
                case VertexAttributeFormat.Float32:
                    BitConverter.TryWriteBytes(at, uv.x);
                    BitConverter.TryWriteBytes(at.Slice(4), uv.y);
                    return true;
                case VertexAttributeFormat.Float16:
                    BitConverter.TryWriteBytes(at, Mathf.FloatToHalf(uv.x));
                    BitConverter.TryWriteBytes(at.Slice(2), Mathf.FloatToHalf(uv.y));
                    return true;
                case VertexAttributeFormat.UNorm16:
                    BitConverter.TryWriteBytes(at, (ushort)Mathf.RoundToInt(Mathf.Clamp01(uv.x) * ushort.MaxValue));
                    BitConverter.TryWriteBytes(at.Slice(2), (ushort)Mathf.RoundToInt(Mathf.Clamp01(uv.y) * ushort.MaxValue));
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Fails a skinned mesh whose bone weights read as none, rather than return a mesh that silently stops
        /// skinning. Unity reads them from every mesh with BlendIndices, even one made non-readable with
        /// UploadMeshData(true), in edit and Play Mode (#46), so this guards against an engine that stops doing so.
        /// </summary>
        internal static void RequireBoneWeights(int bonesPerVertexLength, bool hasBlendIndices, string meshName)
        {
            if (bonesPerVertexLength == 0 && hasBlendIndices)
                throw new InvalidOperationException($"Couldn't read the bone weights of {meshName}.");
        }

        // Through the bone-weight API rather than the vertex streams, which hold at most four bones per vertex.
        static void CopyBoneWeights(Mesh.MeshData data, Mesh source, Mesh mesh, int[] sourceOf)
        {
            mesh.bindposes = source.bindposes;
            using var bonesPerVertex = source.GetBonesPerVertex();
            RequireBoneWeights(bonesPerVertex.Length, data.HasVertexAttribute(VertexAttribute.BlendIndices), source.name);
            if (bonesPerVertex.Length == 0) return;
            using var weights = source.GetAllBoneWeights();
            var start = new int[bonesPerVertex.Length];
            for (var v = 1; v < start.Length; v++) start[v] = start[v - 1] + bonesPerVertex[v - 1];

            var copiedPerVertex = new byte[sourceOf.Length];
            var copiedWeights = new List<BoneWeight1>();
            for (var v = 0; v < sourceOf.Length; v++)
            {
                var from = sourceOf[v];
                copiedPerVertex[v] = bonesPerVertex[from];
                for (var b = 0; b < bonesPerVertex[from]; b++) copiedWeights.Add(weights[start[from] + b]);
            }
            using var perVertexArray = new NativeArray<byte>(copiedPerVertex, Allocator.Temp);
            using var weightArray = new NativeArray<BoneWeight1>(copiedWeights.ToArray(), Allocator.Temp);
            mesh.SetBoneWeights(perVertexArray, weightArray);
        }

        // True when a blend-shape frame moves a vertex with extra copies: its position, normal or tangent.
        static bool AnyMoves(Mesh source, int[] sourceOf, int vertexCount)
        {
            if (source.blendShapeCount == 0) return false;
            var split = new List<int>();
            for (var v = vertexCount; v < sourceOf.Length; v++) split.Add(sourceOf[v]);
            var dv = new Vector3[source.vertexCount];
            var dn = new Vector3[source.vertexCount];
            var dt = new Vector3[source.vertexCount];
            for (var shape = 0; shape < source.blendShapeCount; shape++)
            for (var frame = 0; frame < source.GetBlendShapeFrameCount(shape); frame++)
            {
                source.GetBlendShapeFrameVertices(shape, frame, dv, dn, dt);
                foreach (var v in split)
                    if (Moves(dv[v], dn[v], dt[v])) return true;
            }
            return false;
        }

        /// <summary>
        /// True when a blend-shape frame's deltas for a vertex move it at all. Exact rather than Vector3 !=, which
        /// calls a delta under 1e-5 long zero, so an appended copy can never lose a delta. Unity drops such a delta
        /// when it stores the frame unless another of the vertex's deltas is longer, so today the two agree.
        /// </summary>
        internal static bool Moves(Vector3 dv, Vector3 dn, Vector3 dt) => NonZero(dv) || NonZero(dn) || NonZero(dt);

        static bool NonZero(Vector3 v) => v.x != 0 || v.y != 0 || v.z != 0;

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

        // Excluded submeshes keep their order; the merged ones become one, where the first of them was, unless
        // they keep their places too.
        static (int[] SubmeshOf, int? Merged) WriteSubmeshes(Mesh.MeshData data, Mesh mesh,
            IReadOnlyList<Vector2[]?> atlasUvs, int[][] corners, bool keepSubmeshes)
        {
            var submeshOf = new int[data.subMeshCount];
            var written = new List<(List<int> Indices, MeshTopology Topology)>();
            int? merged = null;
            for (var s = 0; s < data.subMeshCount; s++)
            {
                if (atlasUvs[s] == null || keepSubmeshes)
                {
                    if (atlasUvs[s] != null) merged ??= written.Count;
                    submeshOf[s] = written.Count;
                    written.Add((new List<int>(corners[s]), data.GetSubMesh(s).topology));
                    continue;
                }
                if (merged == null)
                {
                    merged = written.Count;
                    written.Add((new List<int>(), MeshTopology.Triangles));
                }
                submeshOf[s] = merged.Value;
                written[merged.Value].Indices.AddRange(corners[s]);
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
            // Where each source vertex's extra copies start, once Assign has run.
            int[]? firstExtra;

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
            /// Lays the copies out: each source vertex's first copy where the vertex was, then the
            /// extra copies, in source vertex order. Returns each copy's source vertex and UV0 (null:
            /// its own).
            /// </summary>
            public (int[] SourceOf, Vector2?[] Uv0Of) Assign()
            {
                firstExtra = new int[wanted.Length];
                var count = wanted.Length;
                for (var v = 0; v < wanted.Length; v++)
                {
                    firstExtra[v] = count;
                    count += Math.Max(0, (wanted[v]?.Count ?? 1) - 1);
                }
                var sourceOf = new int[count];
                var uv0Of = new Vector2?[count];
                for (var v = 0; v < wanted.Length; v++)
                {
                    var copies = wanted[v];
                    sourceOf[v] = v;
                    uv0Of[v] = copies?[0];
                    for (var c = 1; c < (copies?.Count ?? 1); c++)
                    {
                        sourceOf[IndexOf(v, c)] = v;
                        uv0Of[IndexOf(v, c)] = copies![c];
                    }
                }
                return (sourceOf, uv0Of);
            }

            /// <summary>Where a copy of the source vertex sits once <see cref="Assign"/> has laid them out.</summary>
            public int IndexOf(int vertex, int copy) => copy == 0 ? vertex : firstExtra![vertex] + copy - 1;
        }
    }

    /// <summary>A mesh rewritten to sample the atlas, and where its source submeshes went.</summary>
    internal sealed class RewrittenMesh
    {
        public RewrittenMesh(Mesh mesh, int[] submeshOf, int? mergedSubmesh, RewritePath path)
        {
            Mesh = mesh;
            SubmeshOf = submeshOf;
            MergedSubmesh = mergedSubmesh;
            Path = path;
        }

        public Mesh Mesh { get; }

        /// <summary>For each source submesh, the rewritten mesh's submesh that holds its triangles.</summary>
        public IReadOnlyList<int> SubmeshOf { get; }

        /// <summary>
        /// The submesh that takes the merged material, the first of them when merged submeshes keep their
        /// places, or null when nothing on the mesh was merged.
        /// </summary>
        public int? MergedSubmesh { get; }

        /// <summary>How the mesh was made, which decides how long a mesh with many blend-shape frames takes.</summary>
        public RewritePath Path { get; }
    }

    /// <summary>How <see cref="Uv0Rewriter"/> made a rewritten mesh.</summary>
    internal enum RewritePath
    {
        /// <summary>No vertex split: a native copy with UV0 rewritten.</summary>
        Copied,

        /// <summary>
        /// Vertices split, but none that a blend shape moves: a native copy with the extra copies appended,
        /// so the blend shapes stay as they were.
        /// </summary>
        Appended,

        /// <summary>A vertex that a blend shape moves split, so every blend-shape frame was added again.</summary>
        Rebuilt,
    }
}
