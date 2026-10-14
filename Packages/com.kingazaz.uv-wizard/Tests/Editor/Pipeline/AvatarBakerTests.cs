#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UVWizard.Core.Pipeline;
using Object = UnityEngine.Object;

namespace UVWizard.Core.Tests.Pipeline
{
    [Category("GPU")]
    public class AvatarBakerTests
    {
        readonly List<Object> created = new List<Object>();

        [SetUp]
        public void RequireGpu()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("Baking renders on the GPU, and this editor has no graphics device (-nographics).");
        }

        [TearDown]
        public void DestroyObjects()
        {
            foreach (var o in created) Object.DestroyImmediate(o);
            created.Clear();
        }

        [Test]
        public void MergedTrianglesSampleTheSameColoursAtTheirCentroids()
        {
            // Two renderers and three source materials, one of them with a scale and offset.
            var a = Standard(Gradient(16, 0), Color.white);
            var b = Standard(Gradient(32, 1), Color.white);
            var c = Standard(Gradient(16, 2), Color.white, scaleOffset: new Vector4(0.5f, 0.5f, 0.25f, 0.5f));
            var first = MeshRenderer(Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }, new[] { Quad(1, 0.2f, 0.3f, 0.9f, 0.8f) }), a, b);
            var second = MeshRenderer(Mesh(new[] { Quad(2, 0.05f, 0.4f, 0.55f, 0.95f) }), c);
            var before = Centroids(first).Concat(Centroids(second)).Select(SourceColour).ToArray();

            var result = Bake(first, second);

            Assert.That(result.Fits, Is.True);
            Assert.That(first.sharedMaterials, Is.EqualTo(new[] { result.MergedMaterial }));
            Assert.That(second.sharedMaterials, Is.EqualTo(new[] { result.MergedMaterial }));
            var after = Centroids(first).Concat(Centroids(second)).Select(centroid => result.Atlas!.GetPixelBilinear(centroid.Uv.x, centroid.Uv.y)).ToArray();
            for (var k = 0; k < before.Length; k++)
                AssertClose(before[k], after[k], $"triangle {k}");
        }

        [Test]
        public void TheMergedMaterialSamplesTheAtlasUntinted()
        {
            var renderer = MeshRenderer(Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }), Standard(Gradient(16, 0), new Color(1, 0.5f, 0.25f)));

            var result = Bake(renderer);

            Assert.That(result.MergedMaterial!.shader, Is.SameAs(Shader.Find("Standard")));
            Assert.That(result.MergedMaterial.mainTexture, Is.SameAs(result.Atlas));
            Assert.That(result.MergedMaterial.color, Is.EqualTo(Color.white));
        }

        [Test]
        public void AnExcludedSlotKeepsItsMaterialAndUv0()
        {
            // The second slot's island spans five tiles, over the repeat limit.
            var merged = Standard(Gradient(16, 0), Color.white);
            var repeated = Standard(Gradient(16, 1), Color.white);
            var mesh = Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }, new[] { Quad(1, 0, 0, 5, 1) });
            var renderer = MeshRenderer(mesh, merged, repeated);

            var result = Bake(renderer);

            var rewritten = Filter(renderer).sharedMesh;
            Assert.That(renderer.sharedMaterials, Is.EqualTo(new[] { result.MergedMaterial, repeated }));
            Assert.That(CornerUvs(rewritten, 1), Is.EqualTo(CornerUvs(mesh, 1)));
            Assert.That(result.Excluded.Select(note => (note.Renderer, note.Slot, note.Material)),
                Is.EqualTo(new[] { ((Renderer)renderer, 1, repeated) }));
            Assert.That(result.Excluded[0].Reason, Does.Contain("repeat"));
        }

        [Test]
        public void AMaterialItsReaderExcludesKeepsItsSlot()
        {
            // Standard's detail maps sampled from UV1 can't be remapped.
            var merged = Standard(Gradient(16, 0), Color.white);
            var detailed = Standard(Gradient(16, 1), Color.white);
            detailed.EnableKeyword("_DETAIL_MULX2");
            detailed.SetFloat("_UVSec", 1);
            var first = MeshRenderer(Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }), merged);
            var second = MeshRenderer(Mesh(new[] { Quad(1, 0.1f, 0.1f, 0.6f, 0.7f) }), detailed);

            var result = Bake(first, second);

            Assert.That(second.sharedMaterials, Is.EqualTo(new[] { detailed }));
            Assert.That(result.Excluded.Select(note => note.Material), Is.EqualTo(new[] { detailed }));
            Assert.That(result.Excluded[0].Reason, Does.Contain("UV1"));
        }

        [Test]
        public void ATextureTiledPastTheRepeatLimitIsExcluded()
        {
            // The island covers half a UV tile, but the texture is tiled 20 times across it: 10 repeats.
            var tiled = Standard(Gradient(16, 0), Color.white, scaleOffset: new Vector4(20, 1, 0, 0));
            var renderer = MeshRenderer(Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }), tiled);

            var result = Bake(renderer);

            Assert.That(renderer.sharedMaterials, Is.EqualTo(new[] { tiled }));
            Assert.That(result.Excluded.Select(note => note.Reason), Has.Some.Contains("repeat"));
        }

        [Test]
        public void AUv0FormatThatCantHoldAtlasUvsExcludesTheSlotBeforeAnythingChanges()
        {
            var merged = MeshRenderer(Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }), Standard(Gradient(16, 0), Color.white));
            var bytes = Keep(new Mesh());
            bytes.SetVertexBufferParams(3,
                new VertexAttributeDescriptor(VertexAttribute.Position, stream: 0),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.UNorm8, 4, stream: 1));
            bytes.SetVertexBufferData(new[] { Vector3.zero, Vector3.right, Vector3.up }, 0, 0, 3, 0);
            bytes.SetVertexBufferData(new byte[] { 0, 0, 0, 0, 255, 0, 0, 0, 0, 255, 0, 0 }, 0, 0, 12, 1);
            bytes.SetIndexBufferParams(3, IndexFormat.UInt16);
            bytes.SetIndexBufferData(new ushort[] { 0, 1, 2 }, 0, 0, 3);
            bytes.subMeshCount = 1;
            bytes.SetSubMesh(0, new SubMeshDescriptor(0, 3));
            var material = Standard(Gradient(16, 1), Color.white);
            var unwritable = MeshRenderer(bytes, material);

            var result = Bake(merged, unwritable);

            Assert.That(Filter(unwritable).sharedMesh, Is.SameAs(bytes));
            Assert.That(unwritable.sharedMaterials, Is.EqualTo(new[] { material }));
            Assert.That(result.Excluded.Select(note => note.Reason), Has.Some.Contains("UV0"));
            Assert.That(merged.sharedMaterials, Is.EqualTo(new[] { result.MergedMaterial }));
        }

        [Test]
        public void ALineSubmeshIsExcluded()
        {
            var mesh = Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }, new[] { Quad(1, 0.2f, 0.3f, 0.9f, 0.8f) });
            mesh.SetIndices(new[] { 4, 5 }, MeshTopology.Lines, 1);
            var lines = Standard(Gradient(16, 1), Color.white);
            var renderer = MeshRenderer(mesh, Standard(Gradient(16, 0), Color.white), lines);

            var result = Bake(renderer);

            Assert.That(renderer.sharedMaterials[1], Is.SameAs(lines));
            Assert.That(result.Excluded.Select(note => note.Material), Is.EqualTo(new[] { lines }));
        }

        [Test]
        public void AMeshWithoutUv0IsExcluded()
        {
            var mesh = Keep(new Mesh());
            mesh.SetVertices(new[] { Vector3.zero, Vector3.right, Vector3.up });
            mesh.SetTriangles(new[] { 0, 1, 2 }, 0);
            var material = Standard(Gradient(16, 0), Color.white);
            var renderer = MeshRenderer(mesh, material);

            var result = Bake(renderer);

            Assert.That(renderer.sharedMaterials, Is.EqualTo(new[] { material }));
            Assert.That(result.Excluded.Select(note => note.Reason), Has.Some.Contains("UV0"));
        }

        [Test]
        public void AnEmptySlotIsLeftAlone()
        {
            var mesh = Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }, new[] { Quad(1, 0.2f, 0.3f, 0.9f, 0.8f) });
            var renderer = MeshRenderer(mesh, Standard(Gradient(16, 0), Color.white), null!);

            var result = Bake(renderer);

            Assert.That(renderer.sharedMaterials, Is.EqualTo(new[] { result.MergedMaterial, null }));
            Assert.That(result.Excluded, Is.Empty);
        }

        [Test]
        public void AnUntexturedSourceBakesItsTint()
        {
            var tint = new Color(0.8f, 0.3f, 0.2f);
            var plain = Keep(new Material(Shader.Find("Standard")) { color = tint });
            var renderer = MeshRenderer(Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }), plain);

            var result = Bake(renderer);

            foreach (var centroid in Centroids(renderer))
                AssertClose(tint, result.Atlas!.GetPixelBilinear(centroid.Uv.x, centroid.Uv.y), "centroid");
        }

        [Test]
        public void TheRepresentativeCountsStackedIslandsOnce()
        {
            // The first source stacks two copies of a 0.3 x 0.3 quad: 0.18 of summed triangle area, but one
            // 0.09 chart. The second covers one 0.4 x 0.4 quad, 0.16, and so is the larger chart.
            var stacked = Standard(Gradient(16, 0), Color.white);
            var single = Standard(Gradient(16, 1), Color.white);
            var mesh = Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.4f, 0.4f), Quad(1, 0.1f, 0.1f, 0.4f, 0.4f) }, new[] { Quad(2, 0.5f, 0.5f, 0.9f, 0.9f) });
            var renderer = MeshRenderer(mesh, stacked, single);

            var result = Bake(renderer);

            Assert.That(result.Representative, Is.SameAs(single));
        }

        [Test]
        public void AnEmptySubmeshBesideAnotherMergedShaderSourceStillBakes()
        {
            // Both sources use the merged shader, but the first slot's submesh has no triangles, so it has no charts.
            var empty = Standard(Gradient(16, 0), Color.white);
            var drawn = Standard(Gradient(16, 1), Color.white);
            var mesh = Mesh(Array.Empty<(Vector3[], Vector2[])>(), new[] { Quad(1, 0.1f, 0.1f, 0.6f, 0.7f) });
            var renderer = MeshRenderer(mesh, empty, drawn);

            BakeResult? result = null;
            Assert.That(() => result = Bake(renderer), Throws.Nothing);
            Assert.That(result!.Representative, Is.SameAs(drawn));
            Assert.That(renderer.sharedMaterials, Is.EqualTo(new[] { result.MergedMaterial }));
        }

        [Test]
        public void AMergedShaderSourceThatDrawsNothingDoesntRepresent()
        {
            // The only Standard source sits on an empty submesh; the drawn one uses Unlit/Texture.
            var ramp = Gradient(8, 3);
            var empty = Standard(Gradient(16, 0), Color.white);
            var unlit = Keep(new Material(Shader.Find("Unlit/Texture")) { mainTexture = Gradient(16, 1) });
            var mesh = Mesh(Array.Empty<(Vector3[], Vector2[])>(), new[] { Quad(1, 0.1f, 0.1f, 0.6f, 0.7f) });
            var renderer = MeshRenderer(mesh, empty, unlit);
            var options = new BakeOptions(Shader.Find("Standard"), rampProperty: "_DetailAlbedoMap", defaultRamp: ramp);

            var result = Bake(options, renderer);

            Assert.That(result.Representative, Is.Null);
            Assert.That(result.MergedMaterial!.GetTexture("_DetailAlbedoMap"), Is.SameAs(ramp));
        }

        [Test]
        public void AFlaggedMaterialIsMergedAndListed()
        {
            var transparent = Standard(Gradient(16, 0), Color.white);
            transparent.SetFloat("_Mode", 3);
            transparent.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            var renderer = MeshRenderer(Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }), transparent);

            var result = Bake(renderer);

            Assert.That(renderer.sharedMaterials, Is.EqualTo(new[] { result.MergedMaterial }));
            Assert.That(result.Flagged.Select(note => note.Material), Is.EqualTo(new[] { transparent }));
            Assert.That(result.Flagged[0].Reason, Does.Contain("opaque"));
        }

        [Test]
        public void TheRepresentativeIsTheMergedShaderSourceWithTheMostChartArea()
        {
            // Both sources use the merged shader; the second covers more of its texture.
            var small = Standard(Gradient(16, 0), Color.white);
            small.SetFloat("_Glossiness", 0.1f);
            var large = Standard(Gradient(16, 1), Color.white);
            large.SetFloat("_Glossiness", 0.9f);
            var renderer = MeshRenderer(Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.3f, 0.3f) }, new[] { Quad(1, 0.1f, 0.1f, 0.9f, 0.9f) }), small, large);

            var result = Bake(new BakeOptions(Shader.Find("Standard"), representativeProperties: new[] { "_Glossiness" }), renderer);

            Assert.That(result.Representative, Is.SameAs(large));
            Assert.That(result.MergedMaterial!.GetFloat("_Glossiness"), Is.EqualTo(0.9f));
        }

        [Test]
        public void WithoutAMergedShaderSourceTheDefaultRampIsUsed()
        {
            // The source uses Unlit/Texture, not the merged shader. Standard's detail map stands in for the ramp slot.
            var ramp = Gradient(8, 3);
            var unlit = Keep(new Material(Shader.Find("Unlit/Texture")) { mainTexture = Gradient(16, 0) });
            var renderer = MeshRenderer(Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }), unlit);
            var options = new BakeOptions(Shader.Find("Standard"), rampProperty: "_DetailAlbedoMap", defaultRamp: ramp);

            var result = Bake(options, renderer);

            Assert.That(result.Representative, Is.Null);
            Assert.That(result.MergedMaterial!.GetTexture("_DetailAlbedoMap"), Is.SameAs(ramp));
        }

        [Test]
        public void ChartsThatFitAtNoScaleLeaveEveryRendererUnchanged()
        {
            // 320 separate quads in a 64 px atlas of 256 cells: no scale fits them. On a 1024 px texture each
            // quad covers its own overlap cells, so each is its own chart.
            var quads = Enumerable.Range(0, 320).Select(i => Quad(0, (i % 20) / 20f, (i / 20) / 16f, (i % 20 + 0.4f) / 20f, (i / 20 + 0.4f) / 16f)).ToArray();
            var material = Standard(Gradient(1024, 0), Color.white);
            var mesh = Mesh(quads);
            var renderer = MeshRenderer(mesh, material);

            var result = Bake(new BakeOptions(Shader.Find("Standard"), atlasSize: 64, padding: 0), renderer);

            Assert.That(result.Fits, Is.False);
            Assert.That(result.MergedMaterial, Is.Null);
            Assert.That(renderer.sharedMaterials, Is.EqualTo(new[] { material }));
            Assert.That(Filter(renderer).sharedMesh, Is.SameAs(mesh));
        }

        [Test]
        public void ASkinnedMeshIsReplacedAndKeepsItsBlendShapeWeights()
        {
            var mesh = Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) });
            mesh.AddBlendShapeFrame("smile", 100, Enumerable.Repeat(Vector3.up, mesh.vertexCount).ToArray(), null, null);
            var renderer = Keep(new GameObject("skinned")).AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.sharedMaterials = new[] { Standard(Gradient(16, 0), Color.white) };
            renderer.SetBlendShapeWeight(0, 40);

            var result = Bake(renderer);

            Assert.That(result.ReplacedMeshes, Is.EqualTo(new[] { (mesh, renderer.sharedMesh) }));
            Assert.That(renderer.sharedMesh, Is.Not.SameAs(mesh));
            Assert.That(renderer.GetBlendShapeWeight(0), Is.EqualTo(40));
        }

        [Test]
        public void ExtraMaterialSlotsKeepTheLastSubmeshOutOfTheAtlas()
        {
            // A third material draws the second submesh again, so that submesh must keep its UV0.
            var mesh = Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }, new[] { Quad(1, 0.2f, 0.3f, 0.9f, 0.8f) });
            var first = Standard(Gradient(16, 0), Color.white);
            var second = Standard(Gradient(16, 1), Color.white);
            var overlay = Standard(Gradient(16, 2), Color.white);
            var renderer = MeshRenderer(mesh, first, second, overlay);

            var result = Bake(renderer);

            Assert.That(renderer.sharedMaterials, Is.EqualTo(new[] { result.MergedMaterial, second, overlay }));
            Assert.That(CornerUvs(Filter(renderer).sharedMesh, 1), Is.EqualTo(CornerUvs(mesh, 1)));
            Assert.That(result.Excluded.Select(note => note.Material), Is.EqualTo(new[] { second }));
        }

        [Test]
        public void TheSameRenderersBakeTheSameWay()
        {
            Material A() => Standard(Gradient(16, 0), Color.white);
            Material B() => Standard(Gradient(32, 1), Color.white);
            var mesh = Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }, new[] { Quad(1, 0.2f, 0.3f, 0.9f, 0.8f) });
            var first = MeshRenderer(mesh, A(), B());
            var second = MeshRenderer(mesh, A(), B());

            Bake(first);
            Bake(second);

            Assert.That(CornerUvs(Filter(second).sharedMesh, 0), Is.EqualTo(CornerUvs(Filter(first).sharedMesh, 0)));
        }

        BakeResult Bake(params Renderer[] renderers) => Bake(new BakeOptions(Shader.Find("Standard")), renderers);

        BakeResult Bake(BakeOptions options, params Renderer[] renderers)
        {
            var result = AvatarBaker.Bake(renderers, options);
            if (result.MergedMaterial != null) created.Add(result.MergedMaterial);
            if (result.Atlas != null) created.Add(result.Atlas);
            foreach (var (_, rewritten) in result.ReplacedMeshes) created.Add(rewritten);
            return result;
        }

        T Keep<T>(T o) where T : Object
        {
            created.Add(o);
            return o;
        }

        // A readable sRGB texture whose colour changes smoothly across it, differently per seed.
        Texture2D Gradient(int size, int seed)
        {
            var texture = Keep(new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false, linear: false)
                { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear });
            var texels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
                texels[y * size + x] = new Color32((byte)(40 + 180 * x / size), (byte)(40 + 180 * y / size),
                    (byte)(60 + 50 * seed), 255);
            texture.SetPixels32(texels);
            texture.Apply();
            return texture;
        }

        Material Standard(Texture2D texture, Color tint, Vector4? scaleOffset = null)
        {
            var material = Keep(new Material(Shader.Find("Standard")) { mainTexture = texture, color = tint });
            var st = scaleOffset ?? new Vector4(1, 1, 0, 0);
            material.mainTextureScale = new Vector2(st.x, st.y);
            material.mainTextureOffset = new Vector2(st.z, st.w);
            return material;
        }

        // A quad at depth z covering the given UV rectangle, as four corners: position = UV.
        static (Vector3[] Positions, Vector2[] Uvs) Quad(float z, float uMin, float vMin, float uMax, float vMax)
        {
            var uvs = new[] { new Vector2(uMin, vMin), new Vector2(uMax, vMin), new Vector2(uMin, vMax), new Vector2(uMax, vMax) };
            return (uvs.Select(uv => new Vector3(uv.x, uv.y, z)).ToArray(), uvs);
        }

        // One submesh per array of quads, two triangles each.
        Mesh Mesh(params (Vector3[] Positions, Vector2[] Uvs)[][] submeshes)
        {
            var positions = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int[]>();
            foreach (var quads in submeshes)
            {
                var indices = new List<int>();
                foreach (var (quadPositions, quadUvs) in quads)
                {
                    var start = positions.Count;
                    positions.AddRange(quadPositions);
                    uvs.AddRange(quadUvs);
                    indices.AddRange(new[] { start, start + 2, start + 1, start + 1, start + 2, start + 3 });
                }
                triangles.Add(indices.ToArray());
            }
            var mesh = Keep(new Mesh { indexFormat = IndexFormat.UInt32 });
            mesh.SetVertices(positions);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = triangles.Count;
            for (var s = 0; s < triangles.Count; s++) mesh.SetTriangles(triangles[s], s);
            mesh.RecalculateNormals();
            return mesh;
        }

        MeshRenderer MeshRenderer(Mesh mesh, params Material[] materials)
        {
            var go = Keep(new GameObject("renderer"));
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = materials;
            return renderer;
        }

        static MeshFilter Filter(Renderer renderer) => renderer.GetComponent<MeshFilter>();

        static Vector2[] CornerUvs(Mesh mesh, int submesh)
        {
            var uvs = mesh.uv;
            return mesh.GetIndices(submesh).Select(i => uvs[i]).ToArray();
        }

        // Each triangle's centroid UV, with the material it's drawn with.
        static IEnumerable<(Vector2 Uv, Material Material)> Centroids(MeshRenderer renderer)
        {
            var mesh = Filter(renderer).sharedMesh;
            var uvs = mesh.uv;
            for (var s = 0; s < mesh.subMeshCount; s++)
            {
                var indices = mesh.GetIndices(s);
                for (var k = 0; k < indices.Length; k += 3)
                    yield return ((uvs[indices[k]] + uvs[indices[k + 1]] + uvs[indices[k + 2]]) / 3, renderer.sharedMaterials[s]);
            }
        }

        static Color SourceColour((Vector2 Uv, Material Material) centroid)
        {
            var material = centroid.Material;
            var uv = Vector2.Scale(centroid.Uv, material.mainTextureScale) + material.mainTextureOffset;
            return ((Texture2D)material.mainTexture).GetPixelBilinear(uv.x, uv.y) * material.color;
        }

        // Within 0.05 per channel: the atlas holds the source at a sub-texel offset.
        static void AssertClose(Color expected, Color actual, string where)
        {
            for (var channel = 0; channel < 3; channel++)
                Assert.That(actual[channel], Is.EqualTo(expected[channel]).Within(0.05f), $"{where}, channel {channel}");
        }
    }
}
