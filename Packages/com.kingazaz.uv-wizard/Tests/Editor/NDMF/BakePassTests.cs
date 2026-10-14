#nullable enable

using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VRC.SDK3.Avatars.Components;
using Object = UnityEngine.Object;

namespace UVWizard.NDMF.Tests
{
    // NDMF and the VRChat SDK aren't in CI's project, so these run only in the dev project.
    public class BakePassTests
    {
        const string ToonStandardName = "VRChat/Mobile/Toon Standard";
        readonly List<Object> created = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("Baking renders on the GPU, and this editor has no graphics device (-nographics).");
            MobileBuild.Forced = false;
        }

        [TearDown]
        public void TearDown()
        {
            MobileBuild.Forced = false;
            // NDMF saves what a build references into its own temporary assets, which it cleans up itself.
            foreach (var o in created)
                if (o != null && !EditorUtility.IsPersistent(o)) Object.DestroyImmediate(o);
            created.Clear();
        }

        [Test]
        public void AMobileBuildGivesEveryMergedSlotOneToonStandardMaterialSamplingTheSameColours()
        {
            var avatar = Avatar(withComponent: true);
            var before = Centroids(avatar).Select(SourceColour).ToArray();

            MobileBuild.Forced = true;
            var errors = Build(avatar);

            Assert.That(errors, Is.Empty);
            var materials = avatar.GetComponentsInChildren<Renderer>().SelectMany(r => r.sharedMaterials).Distinct().ToArray();
            Assert.That(materials, Has.Length.EqualTo(1));
            var merged = materials[0];
            Assert.That(merged.shader.name, Is.EqualTo(ToonStandardName));
            Assert.That(merged.color, Is.EqualTo(Color.white));
            var atlas = Readable((Texture2D)merged.mainTexture);
            var after = Centroids(avatar).Select(c => atlas.GetPixelBilinear(c.Uv.x, c.Uv.y)).ToArray();
            for (var k = 0; k < before.Length; k++)
                for (var channel = 0; channel < 3; channel++)
                    Assert.That(after[k][channel], Is.EqualTo(before[k][channel]).Within(0.08f), $"triangle {k}, channel {channel}");
        }

        [Test]
        public void APcBuildLeavesTheAvatarUnchanged()
        {
            Assume.That(EditorUserBuildSettings.activeBuildTarget, Is.Not.EqualTo(BuildTarget.Android).And.Not.EqualTo(BuildTarget.iOS));
            var avatar = Avatar(withComponent: true);
            var meshes = Meshes(avatar);
            var materials = Materials(avatar);

            Build(avatar);

            Assert.That(Meshes(avatar), Is.EqualTo(meshes));
            Assert.That(Materials(avatar), Is.EqualTo(materials));
        }

        [Test]
        public void AnAvatarWithoutTheComponentIsUnchanged()
        {
            var avatar = Avatar(withComponent: false);
            var meshes = Meshes(avatar);
            var materials = Materials(avatar);

            MobileBuild.Forced = true;
            Build(avatar);

            Assert.That(Meshes(avatar), Is.EqualTo(meshes));
            Assert.That(Materials(avatar), Is.EqualTo(materials));
        }

        [Test]
        public void AToonStandardSourceGivesTheMergedMaterialItsRampAndCulling()
        {
            var avatar = Avatar(withComponent: true);
            // An SDK preset other than the default: an asset, so it keeps its identity through the build.
            var ramp = Resources.Load<Texture2D>("VRChat/ShadowRampToon2Band");
            Assume.That(ramp, Is.Not.Null);
            var toon = Keep(new Material(Shader.Find(ToonStandardName)) { mainTexture = Gradient(16, 4) });
            toon.SetTexture("_Ramp", ramp);
            toon.SetFloat("_Culling", 0);
            toon.SetFloat("_ShadowBoost", 0.75f);
            var hat = avatar.transform.Find("Hat").GetComponent<MeshRenderer>();
            hat.sharedMaterials = new[] { toon };

            MobileBuild.Forced = true;
            Build(avatar);

            var merged = hat.sharedMaterial;
            Assert.That(merged.GetTexture("_Ramp"), Is.SameAs(ramp));
            Assert.That(merged.GetFloat("_Culling"), Is.EqualTo(0));
            Assert.That(merged.GetFloat("_ShadowBoost"), Is.EqualTo(0.75f));
        }

        [Test]
        public void WithoutAToonStandardSourceTheRampIsTheSdksPreset()
        {
            var avatar = Avatar(withComponent: true);

            MobileBuild.Forced = true;
            Build(avatar);

            var merged = avatar.GetComponentInChildren<Renderer>().sharedMaterial;
            Assert.That(merged.GetTexture("_Ramp"), Is.Not.Null);
            Assert.That(merged.GetTexture("_Ramp"), Is.SameAs(Resources.Load<Texture2D>(ToonStandard.DefaultRamp)));
        }

        [Test]
        public void ASourceThatUsesVertexColourTurnsItOnInTheMergedMaterial()
        {
            var avatar = Avatar(withComponent: true);
            var hat = avatar.transform.Find("Hat").GetComponent<MeshRenderer>();
            hat.sharedMaterials = new[] { Keep(new Material(Shader.Find("VRChat/Mobile/Standard Lite")) { mainTexture = Gradient(16, 4) }) };

            MobileBuild.Forced = true;
            Build(avatar);

            Assert.That(hat.sharedMaterial.GetFloat("_VertexColor"), Is.EqualTo(1));
        }

        [Test]
        public void AnExcludedMaterialStaysAndIsReportedAsNonFatal()
        {
            // The hat's island spans five tiles, over the repeat limit. (Not a keyword: NDMF's saving of
            // in-memory materials during the build drops their keywords, so a test material can't carry one.)
            var avatar = Avatar(withComponent: true);
            var hat = avatar.transform.Find("Hat");
            hat.GetComponent<MeshFilter>().sharedMesh = Mesh(new[] { Quad(2, 0, 0, 5, 1) });

            MobileBuild.Forced = true;
            var errors = Build(avatar);

            // The build saves in-memory materials as new objects, so compare the shader, not the reference.
            var body = avatar.transform.Find("Body").GetComponent<SkinnedMeshRenderer>().sharedMaterial;
            var kept = hat.GetComponent<MeshRenderer>().sharedMaterial;
            Assert.That(kept.shader.name, Is.EqualTo("Standard"));
            Assert.That(body.shader.name, Is.EqualTo(ToonStandardName));
            Assert.That(errors.Select(e => (e.TheError.Severity, (e.TheError as SimpleError)?.TitleKey)),
                Is.EqualTo(new[] { (ErrorSeverity.NonFatal, (string?)Messages.Excluded) }));
        }

        // Processes the avatar in place, on the ambient platform, which is VRChat's in a VRChat project.
        static List<ErrorContext> Build(GameObject avatar) =>
            ErrorReport.CaptureErrors(() => AvatarProcessor.ProcessAvatar(avatar));

        // An avatar root with a skinned body of two submeshes and a hat, on Standard materials.
        GameObject Avatar(bool withComponent)
        {
            var root = Keep(new GameObject("Avatar"));
            root.AddComponent<VRCAvatarDescriptor>();
            if (withComponent) root.AddComponent<UVWizardAvatar>();

            var body = new GameObject("Body").AddComponent<SkinnedMeshRenderer>();
            body.transform.SetParent(root.transform);
            body.sharedMesh = Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }, new[] { Quad(1, 0.2f, 0.3f, 0.9f, 0.8f) });
            body.sharedMaterials = new[] { Standard(Gradient(16, 0)), Standard(Gradient(32, 1)) };

            var hat = new GameObject("Hat");
            hat.transform.SetParent(root.transform);
            hat.AddComponent<MeshFilter>().sharedMesh = Mesh(new[] { Quad(2, 0.05f, 0.4f, 0.55f, 0.95f) });
            hat.AddComponent<MeshRenderer>().sharedMaterials = new[] { Standard(Gradient(16, 2)) };
            return root;
        }

        static Mesh?[] Meshes(GameObject avatar) => avatar.GetComponentsInChildren<Renderer>().Select(r => r switch
        {
            SkinnedMeshRenderer skinned => skinned.sharedMesh,
            _ => r.GetComponent<MeshFilter>().sharedMesh,
        }).ToArray();

        static Material[] Materials(GameObject avatar) =>
            avatar.GetComponentsInChildren<Renderer>().SelectMany(r => r.sharedMaterials).ToArray();

        T Keep<T>(T o) where T : Object
        {
            created.Add(o);
            return o;
        }

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

        Material Standard(Texture2D texture) => Keep(new Material(Shader.Find("Standard")) { mainTexture = texture });

        static (Vector3[] Positions, Vector2[] Uvs) Quad(float z, float uMin, float vMin, float uMax, float vMax)
        {
            var uvs = new[] { new Vector2(uMin, vMin), new Vector2(uMax, vMin), new Vector2(uMin, vMax), new Vector2(uMax, vMax) };
            return (uvs.Select(uv => new Vector3(uv.x, uv.y, z)).ToArray(), uvs);
        }

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
            var mesh = Keep(new Mesh());
            mesh.SetVertices(positions);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = triangles.Count;
            for (var s = 0; s < triangles.Count; s++) mesh.SetTriangles(triangles[s], s);
            mesh.RecalculateNormals();
            return mesh;
        }

        // Each drawn triangle's centroid UV and the material it's drawn with, renderer by renderer.
        static IEnumerable<(Vector2 Uv, Material Material)> Centroids(GameObject avatar)
        {
            foreach (var renderer in avatar.GetComponentsInChildren<Renderer>())
            {
                var mesh = renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh : renderer.GetComponent<MeshFilter>().sharedMesh;
                var uvs = mesh.uv;
                for (var s = 0; s < mesh.subMeshCount; s++)
                {
                    var indices = mesh.GetIndices(s);
                    for (var k = 0; k < indices.Length; k += 3)
                        yield return ((uvs[indices[k]] + uvs[indices[k + 1]] + uvs[indices[k + 2]]) / 3,
                            renderer.sharedMaterials[Mathf.Min(s, renderer.sharedMaterials.Length - 1)]);
                }
            }
        }

        static Color SourceColour((Vector2 Uv, Material Material) centroid) =>
            ((Texture2D)centroid.Material.mainTexture).GetPixelBilinear(centroid.Uv.x, centroid.Uv.y) * centroid.Material.color;

        // The compressed atlas decoded on the GPU into a readable copy.
        Texture2D Readable(Texture2D atlas)
        {
            var target = RenderTexture.GetTemporary(atlas.width, atlas.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            try
            {
                Graphics.Blit(atlas, target);
                RenderTexture.active = target;
                var copy = Keep(new Texture2D(atlas.width, atlas.height, TextureFormat.RGBA32, mipChain: false, linear: false));
                copy.ReadPixels(new Rect(0, 0, atlas.width, atlas.height), 0, 0);
                copy.Apply();
                return copy;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
            }
        }
    }
}
