#nullable enable

using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;
using UVWizard.Core.Baking;
using UVWizard.Core.Pipeline;
using UVWizard.Core.Tests;
using VRC.SDK3.Avatars.Components;
using Object = UnityEngine.Object;

namespace UVWizard.NDMF.Tests
{
    // NDMF and the VRChat SDK aren't in CI's project, so these run only in the dev project.
    [Category("GPU")]
    public partial class BakePassTests : BakeFixture
    {
        const string ToonStandardName = "VRChat/Mobile/Toon Standard";

        [SetUp]
        public void SetUp()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("Baking renders on the GPU, and this editor has no graphics device (-nographics).");
            MobileBuild.Override = null;
        }

        [TearDown]
        public void TearDown()
        {
            MobileBuild.Override = null;
        }

        [Test]
        public void AMobileBuildGivesEveryMergedSlotOneToonStandardMaterialSamplingTheSameColours()
        {
            var avatar = Avatar(withComponent: true);
            var before = Centroids(avatar).Select(SourceColour).ToArray();
            var skinned = avatar.GetComponentsInChildren<SkinnedMeshRenderer>();
            var sources = skinned.Select(renderer => renderer.sharedMesh).ToArray();

            MobileBuild.Override = true;
            var errors = BesidesSlots(Build(avatar));

            Assert.That(errors, Is.Empty);
            Assert.That(skinned[1].sharedMesh, Is.Not.SameAs(skinned[0].sharedMesh), "each renderer gets its own rewritten mesh");
            for (var r = 0; r < skinned.Length; r++)
                Assert.That(skinned[r].sharedMesh, Is.Not.SameAs(sources[r]), $"{skinned[r].name} is rewritten");
            var materials = avatar.GetComponentsInChildren<Renderer>().SelectMany(r => r.sharedMaterials).Distinct().ToArray();
            Assert.That(materials, Has.Length.EqualTo(1));
            var merged = materials[0];
            Assert.That(merged.shader.name, Is.EqualTo(ToonStandardName));
            Assert.That(merged.color, Is.EqualTo(Color.white));
            Assert.That(merged.GetFloat("_VertexColor"), Is.EqualTo(0), "no Standard source uses vertex colour");
            var atlas = Readable((Texture2D)merged.mainTexture);
            var after = Centroids(avatar).Select(c => atlas.GetPixelBilinear(c.Uv.x, c.Uv.y)).ToArray();
            for (var k = 0; k < before.Length; k++)
                for (var channel = 0; channel < 3; channel++)
                    Assert.That(after[k][channel], Is.EqualTo(before[k][channel]).Within(0.08f), $"triangle {k}, channel {channel}");
        }

        [Test]
        public void AGlowingToonStandardSourceStillGlowsAndNothingElseDoes()
        {
            // The hat emits its map at strength 1.5. The merged material carries that strength, so the atlas holds
            // the map itself; every other part bakes black.
            var avatar = Avatar(withComponent: true);
            var toon = ToonStandardMaterial(Gradient(16, 4), culling: 2);
            var glow = Gradient(16, 5);
            toon.SetTexture("_EmissionMap", glow);
            toon.SetColor("_EmissionColor", Color.white);
            toon.SetFloat("_EmissionStrength", 1.5f);
            avatar.transform.Find("Hat").GetComponent<MeshRenderer>().sharedMaterials = new[] { toon };
            var glowing = Centroids(avatar).Select(c => c.Material == toon).ToArray();
            var before = Centroids(avatar).Select(c => glow.GetPixelBilinear(c.Uv.x, c.Uv.y)).ToArray();

            MobileBuild.Override = true;
            var errors = BesidesSlots(Build(avatar));

            Assert.That(errors, Is.Empty);
            var merged = avatar.GetComponentInChildren<Renderer>().sharedMaterial;
            Assert.That(merged.GetColor("_EmissionColor"), Is.EqualTo(Color.white));
            Assert.That(merged.GetFloat("_EmissionStrength"), Is.EqualTo(1.5f).Within(1e-4f));
            Assert.That(merged.GetFloat("_EmissionUV"), Is.EqualTo(0));
            var emission = (Texture2D)merged.GetTexture("_EmissionMap");
            Assert.That(emission, Is.Not.Null.And.Not.SameAs(merged.mainTexture));
            Assert.That(emission.format, Is.EqualTo(TextureFormat.ASTC_6x6));
            var readable = Readable(emission);
            var after = Centroids(avatar).Select(c => readable.GetPixelBilinear(c.Uv.x, c.Uv.y)).ToArray();
            Assert.That(glowing, Has.Some.True);
            for (var k = 0; k < after.Length; k++)
            for (var channel = 0; channel < 3; channel++)
                Assert.That(after[k][channel], Is.EqualTo(glowing[k] ? before[k][channel] : 0).Within(0.08f),
                    $"triangle {k}, channel {channel}");
        }

        [Test]
        public void AnAvatarThatDoesntGlowGetsNoEmissionTexture()
        {
            var avatar = Avatar(withComponent: true);

            MobileBuild.Override = true;
            Build(avatar);

            var merged = avatar.GetComponentInChildren<Renderer>().sharedMaterial;
            Assert.That(merged.GetTexture("_EmissionMap"), Is.Null);
            Assert.That(merged.GetColor("_EmissionColor").maxColorComponent, Is.EqualTo(0), "Toon Standard's default: no glow");
        }

        [Test]
        public void ANormalMappedToonStandardSourceTurnsOnTheMergedMaterialsNormalMap()
        {
            var avatar = Avatar(withComponent: true);
            var toon = NormalMapped(ToonStandardMaterial(Gradient(16, 4), culling: 2), NormalGradient(16, 0), 0.5f);
            avatar.transform.Find("Hat").GetComponent<MeshRenderer>().sharedMaterials = new[] { toon };

            MobileBuild.Override = true;
            var errors = BesidesSlots(Build(avatar));

            Assert.That(errors, Is.Empty);
            var merged = avatar.GetComponentInChildren<Renderer>().sharedMaterial;
            Assert.That(merged.IsKeywordEnabled("USE_NORMAL_MAPS"), Is.True);
            Assert.That(merged.GetFloat("_BumpScale"), Is.EqualTo(1), "the atlas holds the source's strength");
            var normals = (Texture2D)merged.GetTexture("_BumpMap");
            Assert.That(normals, Is.Not.Null.And.Not.SameAs(merged.mainTexture));
            Assert.That(normals.format, Is.EqualTo(TextureFormat.ASTC_4x4), "normals band at 6x6, and Good has room for 4x4");
            Assert.That(normals.isDataSRGB, Is.False);
        }

        [Test]
        public void AnAvatarWithoutNormalMapsLeavesTheMergedMaterialsNormalMapOff()
        {
            var avatar = Avatar(withComponent: true);

            MobileBuild.Override = true;
            var errors = BesidesSlots(Build(avatar));

            Assert.That(errors, Is.Empty);
            var merged = avatar.GetComponentInChildren<Renderer>().sharedMaterial;
            Assert.That(merged.shader.name, Is.EqualTo(ToonStandardName), "the bake ran");
            Assert.That(merged.IsKeywordEnabled("USE_NORMAL_MAPS"), Is.False);
            Assert.That(merged.GetTexture("_BumpMap"), Is.Null);
        }

        [TestCase(4)]
        [TestCase(-4)]
        public void ANormalMapTiledMoreFinelyThanTheAlbedoKeepsItsDetail(float tiling)
        {
            // The 32 px checker's 4-texel cells, tiled 4 times, are 1/32 of a UV apart. Counted at its own size
            // the chart would get 32 texels per UV, one per cell, smearing neighbouring cells together. Tiled -4
            // times, mirrored, they're as close.
            var avatar = Avatar(withComponent: true);
            var toon = NormalMapped(ToonStandardMaterial(Gradient(16, 4), culling: 2), NormalChecker(32, 4), 1);
            toon.SetTextureScale("_BumpMap", new Vector2(tiling, tiling));
            var hat = avatar.transform.Find("Hat");
            hat.GetComponent<MeshFilter>().sharedMesh = Grid(1, 6, 32);
            hat.GetComponent<MeshRenderer>().sharedMaterials = new[] { toon };
            var before = Centroids(hat.gameObject).Select(c => Decoded(TiledSample((Texture2D)toon.GetTexture("_BumpMap"), c.Uv, tiling))).ToArray();

            // The checker is in Unity's XYZ layout, so the atlas is too, whatever the editor's build target.
            BakePass.Bake(avatar, new BakeOptions(Shader.Find(ToonStandardName), normalEncoding: NormalEncoding.RGorAG));

            var normals = (Texture2D)hat.GetComponent<MeshRenderer>().sharedMaterial.GetTexture("_BumpMap");
            var after = Centroids(hat.gameObject).Select(c => Decoded(normals.GetPixelBilinear(c.Uv.x, c.Uv.y))).ToArray();
            for (var k = 0; k < before.Length; k++)
                Assert.That(Vector3.Distance(before[k], after[k]), Is.LessThanOrEqualTo(0.08f), $"triangle {k}: {after[k]}, expected {before[k]}");
        }

        [Test]
        public void AnEmissionMapTiledMoreFinelyThanTheAlbedoKeepsItsDetail()
        {
            // As for the normal map: the emission checker's cells are 1/32 of a UV apart.
            var avatar = Avatar(withComponent: true);
            var toon = ToonStandardMaterial(Gradient(16, 4), culling: 2);
            var glow = Checker(32, 4);
            toon.SetTexture("_EmissionMap", glow);
            toon.SetTextureScale("_EmissionMap", new Vector2(4, 4));
            toon.SetColor("_EmissionColor", Color.white);
            toon.SetFloat("_EmissionStrength", 1);
            var hat = avatar.transform.Find("Hat");
            hat.GetComponent<MeshFilter>().sharedMesh = Grid(1, 6, 32);
            hat.GetComponent<MeshRenderer>().sharedMaterials = new[] { toon };
            var before = Centroids(hat.gameObject).Select(c => TiledSample(glow, c.Uv, 4)).ToArray();

            BakePass.Bake(avatar, new BakeOptions(Shader.Find(ToonStandardName)));

            var emission = (Texture2D)hat.GetComponent<MeshRenderer>().sharedMaterial.GetTexture("_EmissionMap");
            var after = Centroids(hat.gameObject).Select(c => emission.GetPixelBilinear(c.Uv.x, c.Uv.y)).ToArray();
            for (var k = 0; k < before.Length; k++)
            for (var channel = 0; channel < 3; channel++)
                Assert.That(after[k][channel], Is.EqualTo(before[k][channel]).Within(0.08f), $"triangle {k}, channel {channel}");
        }

        [Test]
        public void IslandsInTwoTilesOfAnEmissionMapTiledHalfAsOftenEachGetTheirOwnEmission()
        {
            // The albedo repeats once per tile, so on it alone the hat's islands in tiles 0 and 1 would stack into one
            // chart. The emission map repeats every two tiles, so they glow with its left and right halves.
            var avatar = Avatar(withComponent: true);
            var toon = ToonStandardMaterial(Gradient(16, 4), culling: 2);
            var glow = Gradient(16, 1);
            toon.SetTexture("_EmissionMap", glow);
            toon.SetTextureScale("_EmissionMap", new Vector2(0.5f, 1));
            toon.SetColor("_EmissionColor", Color.white);
            toon.SetFloat("_EmissionStrength", 1);
            var hat = avatar.transform.Find("Hat");
            hat.GetComponent<MeshFilter>().sharedMesh = Mesh(new[] { Quad(2, 0.1f, 0.1f, 0.4f, 0.4f), Quad(2, 1.1f, 0.1f, 1.4f, 0.4f) });
            hat.GetComponent<MeshRenderer>().sharedMaterials = new[] { toon };
            var before = Centroids(hat.gameObject).Select(c => glow.GetPixelBilinear(c.Uv.x * 0.5f, c.Uv.y)).ToArray();

            BakePass.Bake(avatar, new BakeOptions(Shader.Find(ToonStandardName)));

            var emission = (Texture2D)hat.GetComponent<MeshRenderer>().sharedMaterial.GetTexture("_EmissionMap");
            var after = Centroids(hat.gameObject).Select(c => emission.GetPixelBilinear(c.Uv.x, c.Uv.y)).ToArray();
            for (var k = 0; k < before.Length; k++)
            for (var channel = 0; channel < 3; channel++)
                Assert.That(after[k][channel], Is.EqualTo(before[k][channel]).Within(0.08f), $"triangle {k}, channel {channel}");
        }

        [Test]
        public void AnAlbedoScaledToZeroStillBakesItsTiledNormalMap()
        {
            // The albedo's zero scale squeezes the hat's chart to a point, leaving the tiling ratio nothing to divide by.
            var avatar = Avatar(withComponent: true);
            var toon = NormalMapped(ToonStandardMaterial(Gradient(16, 4), culling: 2), NormalChecker(32, 4), 1);
            toon.mainTextureScale = Vector2.zero;
            toon.SetTextureScale("_BumpMap", new Vector2(4, 4));
            avatar.transform.Find("Hat").GetComponent<MeshRenderer>().sharedMaterials = new[] { toon };

            MobileBuild.Override = true;
            var errors = BesidesSlots(Build(avatar));

            Assert.That(errors, Is.Empty);
            var merged = avatar.GetComponentInChildren<Renderer>().sharedMaterial;
            Assert.That(merged.shader.name, Is.EqualTo(ToonStandardName), "the bake ran");
            Assert.That(merged.GetTexture("_BumpMap"), Is.Not.Null);
        }

        [Test]
        public void OnlyMobileTargetsBakeUnlessOverridden()
        {
            Assert.That(MobileBuild.IsActiveFor(BuildTarget.Android), Is.True);
            Assert.That(MobileBuild.IsActiveFor(BuildTarget.iOS), Is.True);
            Assert.That(MobileBuild.IsActiveFor(BuildTarget.StandaloneWindows64), Is.False);
            Assert.That(MobileBuild.IsActive, Is.EqualTo(MobileBuild.IsActiveFor(EditorUserBuildSettings.activeBuildTarget)),
                "a build follows the project's target");
            MobileBuild.Override = true;
            Assert.That(MobileBuild.IsActiveFor(BuildTarget.StandaloneWindows64), Is.True, "a manual bake's mobile preview");
            MobileBuild.Override = false;
            Assert.That(MobileBuild.IsActiveFor(BuildTarget.Android), Is.False, "forced to PC");
        }

        [Test]
        public void APcBuildLeavesTheAvatarUnchanged()
        {
            MobileBuild.Override = false;
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

            MobileBuild.Override = true;
            Build(avatar);

            Assert.That(Meshes(avatar), Is.EqualTo(meshes));
            Assert.That(Materials(avatar), Is.EqualTo(materials));
        }

        [Test]
        public void WithoutToonStandardTheBakeIsAnErrorAndChangesNothing()
        {
            var avatar = Avatar(withComponent: true);
            var meshes = Meshes(avatar);
            var materials = Materials(avatar);

            var errors = ErrorReport.CaptureErrors(() => BakePass.Bake(avatar, null));

            Assert.That(Reports(errors), Is.EqualTo(new[] { (ErrorSeverity.Error, (string?)Messages.NoToonStandard) }));
            Assert.That(Meshes(avatar), Is.EqualTo(meshes));
            Assert.That(Materials(avatar), Is.EqualTo(materials));
        }

        [Test]
        public void ChartsThatFitAtNoScaleAreReportedAndChangeNothing()
        {
            // The hat becomes 320 separate quads on a 1024 px texture, each its own chart: no scale fits them
            // in a 64 px atlas.
            var avatar = Avatar(withComponent: true);
            var hat = avatar.transform.Find("Hat");
            hat.GetComponent<MeshFilter>().sharedMesh = Mesh(Enumerable.Range(0, 320)
                .Select(i => Quad(2, (i % 20) / 20f, (i / 20) / 16f, (i % 20 + 0.4f) / 20f, (i / 20 + 0.4f) / 16f)).ToArray());
            hat.GetComponent<MeshRenderer>().sharedMaterials = new[] { Standard(Gradient(1024, 2)) };
            var meshes = Meshes(avatar);
            var materials = Materials(avatar);

            var errors = ErrorReport.CaptureErrors(() =>
                BakePass.Bake(avatar, new BakeOptions(Shader.Find(ToonStandardName), atlasSize: 64, padding: 0)));

            Assert.That(Reports(errors), Is.EqualTo(new[] { (ErrorSeverity.NonFatal, (string?)Messages.NoFit) }));
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

            MobileBuild.Override = true;
            Build(avatar);

            var merged = hat.sharedMaterial;
            Assert.That(merged.GetTexture("_Ramp"), Is.SameAs(ramp));
            Assert.That(merged.GetFloat("_Culling"), Is.EqualTo(0));
            Assert.That(merged.GetFloat("_ShadowBoost"), Is.EqualTo(0.75f));
        }

        // Toon Standard's own default ramp is the same preset, so this checks the outcome; AvatarBakerTests
        // checks that the default ramp is chosen when no source has the merged shader.
        [Test]
        public void WithoutAToonStandardSourceTheRampIsTheSdksPreset()
        {
            var avatar = Avatar(withComponent: true);

            MobileBuild.Override = true;
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

            MobileBuild.Override = true;
            Build(avatar);

            Assert.That(hat.sharedMaterial.GetFloat("_VertexColor"), Is.EqualTo(1));
        }

        [Test]
        public void AnEmptySlotDoesntTurnVertexColourOn()
        {
            // The hat's first submesh draws nothing; its material would use vertex colour.
            var avatar = Avatar(withComponent: true);
            var hat = avatar.transform.Find("Hat");
            hat.GetComponent<MeshFilter>().sharedMesh = Mesh(new (Vector3[], Vector2[])[0], new[] { Quad(2, 0.05f, 0.4f, 0.55f, 0.95f) });
            hat.GetComponent<MeshRenderer>().sharedMaterials = new[]
            {
                Keep(new Material(Shader.Find("VRChat/Mobile/Standard Lite")) { mainTexture = Gradient(16, 4) }),
                Standard(Gradient(16, 2)),
            };

            MobileBuild.Override = true;
            Build(avatar);

            Assert.That(hat.GetComponent<MeshRenderer>().sharedMaterial.GetFloat("_VertexColor"), Is.EqualTo(0));
        }

        [Test]
        public void AnEmptySlotIsntFlaggedForItsCulling()
        {
            // The body is Toon Standard culling back faces and covers the most area. The hat's first submesh draws
            // nothing, on a double-sided Toon Standard material.
            var avatar = Avatar(withComponent: true);
            var body = avatar.transform.Find("Body").GetComponent<SkinnedMeshRenderer>();
            body.sharedMaterials = body.sharedMaterials.Select(source => ToonStandardMaterial(source.mainTexture, culling: 2)).ToArray();
            var hat = avatar.transform.Find("Hat");
            hat.GetComponent<MeshFilter>().sharedMesh = Mesh(new (Vector3[], Vector2[])[0], new[] { Quad(2, 0.05f, 0.4f, 0.55f, 0.95f) });
            hat.GetComponent<MeshRenderer>().sharedMaterials = new[]
                { ToonStandardMaterial(Gradient(16, 4), culling: 0), ToonStandardMaterial(Gradient(16, 2), culling: 2) };

            MobileBuild.Override = true;
            var errors = BesidesSlots(Build(avatar));

            Assert.That(Texts(errors), Is.Empty);
        }

        [Test]
        public void AnExcludedMaterialGetsAToonStandardMaterialOfItsOwnAndIsReportedAsNonFatal()
        {
            // The hat's island spans five tiles, over the repeat limit. (Not a keyword: NDMF's saving of
            // in-memory materials during the build drops their keywords, so a test material can't carry one.)
            var avatar = Avatar(withComponent: true);
            var hat = avatar.transform.Find("Hat");
            hat.GetComponent<MeshFilter>().sharedMesh = Mesh(new[] { Quad(2, 0, 0, 5, 1) });

            MobileBuild.Override = true;
            var errors = Build(avatar);

            // The build saves in-memory materials as new objects, so compare the shader, not the reference.
            var body = avatar.transform.Find("Body").GetComponent<SkinnedMeshRenderer>().sharedMaterial;
            var kept = hat.GetComponent<MeshRenderer>().sharedMaterial;
            Assert.That(kept.shader.name, Is.EqualTo(ToonStandardName));
            Assert.That(kept, Is.Not.SameAs(body), "it isn't merged");
            Assert.That(body.shader.name, Is.EqualTo(ToonStandardName));
            Assert.That(errors.Select(e => (e.TheError.Severity, (e.TheError as SimpleError)?.TitleKey)),
                Is.EqualTo(new[] { (ErrorSeverity.NonFatal, (string?)Messages.Excluded), (ErrorSeverity.NonFatal, Messages.TooManySlots) }),
                "the kept material takes a second slot, which Good doesn't allow");
        }

        [Test]
        public void AMergedSlotCulledUnlikeTheMergedMaterialIsFlagged()
        {
            // The body is Toon Standard culling back faces and covers the most area; the hat is double-sided.
            var avatar = Avatar(withComponent: true);
            var body = avatar.transform.Find("Body").GetComponent<SkinnedMeshRenderer>();
            body.sharedMaterials = body.sharedMaterials.Select(source => ToonStandardMaterial(source.mainTexture, culling: 2)).ToArray();
            var hat = avatar.transform.Find("Hat").GetComponent<MeshRenderer>();
            hat.sharedMaterials = new[] { ToonStandardMaterial(Gradient(16, 4), culling: 0) };

            MobileBuild.Override = true;
            var errors = BesidesSlots(Build(avatar));

            Assert.That(hat.sharedMaterial.GetFloat("_Culling"), Is.EqualTo(2));
            Assert.That(Reports(errors), Is.EqualTo(new[] { (ErrorSeverity.NonFatal, (string?)Messages.Flagged) }));
            Assert.That(Texts(errors).Single(), Does.Contain("cull"));
        }

        [Test]
        public void TurningVertexColourOnFlagsSourcesWhoseMeshesCarryVertexColours()
        {
            // The hat uses vertex colour, so the merged material turns it on; the body's mesh carries red vertex
            // colours that its Standard materials never showed.
            var avatar = Avatar(withComponent: true);
            var hat = avatar.transform.Find("Hat").GetComponent<MeshRenderer>();
            hat.sharedMaterials = new[] { Keep(new Material(Shader.Find("VRChat/Mobile/Standard Lite")) { mainTexture = Gradient(16, 4) }) };
            var body = avatar.transform.Find("Body").GetComponent<SkinnedMeshRenderer>();
            body.sharedMesh.colors = Enumerable.Repeat(Color.red, body.sharedMesh.vertexCount).ToArray();

            MobileBuild.Override = true;
            var errors = BesidesSlots(Build(avatar));

            Assert.That(Reports(errors), Has.All.EqualTo((ErrorSeverity.NonFatal, (string?)Messages.Flagged)));
            Assert.That(Texts(errors), Has.Count.EqualTo(4), "two body slots on each of the two skinned renderers");
            Assert.That(Texts(errors), Has.All.Contain("vertex colour"));
        }

        [Test]
        public void AToggleThatSwapsAMaterialExcludesItAndTheSlotsOriginalAndTheRestMerge()
        {
            // The body's third slot is the eyes, which an FX toggle swaps to red. Its first two slots merge into
            // one, so the eyes move from slot 2 to slot 1, and the toggle has to follow them.
            var avatar = Avatar(withComponent: true);
            var body = avatar.transform.Find("Body").GetComponent<SkinnedMeshRenderer>();
            body.sharedMesh = Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }, new[] { Quad(1, 0.2f, 0.3f, 0.9f, 0.8f) },
                new[] { Quad(4, 0.1f, 0.1f, 0.3f, 0.3f) });
            var eyes = Standard(Gradient(16, 4));
            eyes.name = "Eyes blue";
            var red = Standard(Gradient(16, 5));
            red.name = "Eyes red";
            body.sharedMaterials = new[] { body.sharedMaterials[0], body.sharedMaterials[1], eyes };
            var toggle = Keep(new AnimationClip { name = "Red eyes on" });
            AnimationUtility.SetObjectReferenceCurve(toggle, Slot("Body", 2),
                new[] { new ObjectReferenceKeyframe { time = 0, value = red } });
            WithFx(avatar, toggle);

            MobileBuild.Override = true;
            var errors = BesidesSlots(Build(avatar));
            Assert.That(body.sharedMaterials.Select(m => m.shader.name), Is.EqualTo(new[] { ToonStandardName, ToonStandardName }));
            Assert.That(body.sharedMaterials[1].name, Is.EqualTo("Eyes blue (UV Wizard)"));
            AssertTheRestAreOnToonStandard(avatar);
            Assert.That(Reports(errors), Has.All.EqualTo((ErrorSeverity.NonFatal, (string?)Messages.Excluded)));
            Assert.That(Texts(errors), Has.Count.EqualTo(2));
            Assert.That(Texts(errors), Has.Some.Contain("Eyes blue").And.Some.Contain("Eyes red"));
            Assert.That(Texts(errors), Has.All.Contain("Red eyes on"));
            var swaps = FxClips(avatar).SelectMany(clip => AnimationUtility.GetObjectReferenceCurveBindings(clip)
                .Select(binding => (binding.path, binding.propertyName,
                    AnimationUtility.GetObjectReferenceCurve(clip, binding)[0].value.name)));
            Assert.That(swaps, Is.EqualTo(new[] { ("Body", "m_Materials.Array.data[1]", "Eyes red (UV Wizard)") }));
            Assert.That(FxClips(avatar).SelectMany(SwappedIn).Select(m => m.shader.name), Has.All.EqualTo(ToonStandardName));
        }

        [Test]
        public void AnAnimatedEmissionColourExcludesThatRenderersMaterials()
        {
            // A glow toggle on the body changes its materials' emission, which would change every merged material.
            var avatar = Avatar(withComponent: true);
            var glow = Keep(new AnimationClip { name = "Body glow" });
            glow.SetCurve("Body", typeof(SkinnedMeshRenderer), "material._EmissionColor.r", AnimationCurve.Constant(0, 1, 1));
            glow.SetCurve("Tail", typeof(GameObject), "m_IsActive", AnimationCurve.Constant(0, 1, 0));
            WithFx(avatar, glow);

            MobileBuild.Override = true;
            var errors = BesidesSlots(Build(avatar));

            var body = avatar.transform.Find("Body").GetComponent<SkinnedMeshRenderer>();
            Assert.That(body.sharedMaterials.Select(m => m.shader.name), Is.EqualTo(new[] { ToonStandardName, ToonStandardName }));
            Assert.That(body.sharedMaterials.Distinct().Count(), Is.EqualTo(2), "each keeps a material of its own");
            AssertTheRestAreOnToonStandard(avatar);
            Assert.That(Reports(errors), Is.EqualTo(Enumerable.Repeat((ErrorSeverity.NonFatal, (string?)Messages.Excluded), 2)
                .Concat(Enumerable.Repeat((ErrorSeverity.NonFatal, (string?)Messages.Flagged), 2))));
            Assert.That(Texts(errors).Take(2), Has.All.Contain("Body glow").And.All.Contain("_EmissionColor"));
            Assert.That(Texts(errors).Skip(2), Has.All.Contain("material properties"),
                "the glow now changes Toon Standard materials");
        }

        [Test]
        public void AnAvatarWhoseAnimationsDontTouchMaterialsHasNoExclusions()
        {
            // A hat toggle turns the hat off and on; it changes no material. Its stray colour curve names a skinned
            // renderer, which the hat doesn't have, so it animates nothing.
            var avatar = Avatar(withComponent: true);
            var hatOff = Keep(new AnimationClip { name = "Hat off" });
            hatOff.SetCurve("Hat", typeof(GameObject), "m_IsActive", AnimationCurve.Constant(0, 1, 0));
            hatOff.SetCurve("Hat", typeof(SkinnedMeshRenderer), "material._Color.r", AnimationCurve.Constant(0, 1, 0));
            WithFx(avatar, hatOff);

            MobileBuild.Override = true;
            var errors = BesidesSlots(Build(avatar));

            Assert.That(errors, Is.Empty);
            Assert.That(Materials(avatar).Select(m => m.shader.name), Has.All.EqualTo(ToonStandardName));
        }

        [Test]
        public void SwapsOnTwoSlotsThatBothMoveEachFollowTheirOwnSlot()
        {
            // The eyes (slot 2) and mouth (slot 3) move down one when the first two slots merge, the mouth onto the
            // eyes' old slot. A malformed curve on slot -1 animates nothing, and mustn't stop the build.
            var avatar = Avatar(withComponent: true);
            var body = avatar.transform.Find("Body").GetComponent<SkinnedMeshRenderer>();
            body.sharedMesh = Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }, new[] { Quad(1, 0.2f, 0.3f, 0.9f, 0.8f) },
                new[] { Quad(4, 0.1f, 0.1f, 0.3f, 0.3f) }, new[] { Quad(5, 0.6f, 0.6f, 0.8f, 0.8f) });
            body.sharedMaterials = new[]
                { body.sharedMaterials[0], body.sharedMaterials[1], Standard(Gradient(16, 4)), Standard(Gradient(16, 5)) };
            var toggle = Keep(new AnimationClip { name = "Face colours" });
            foreach (var (slot, colour) in new[] { (3, "Red mouth"), (2, "Red eyes"), (-1, "Stray") })
            {
                var red = Standard(Gradient(16, 6));
                red.name = colour;
                AnimationUtility.SetObjectReferenceCurve(toggle, Slot("Body", slot),
                    new[] { new ObjectReferenceKeyframe { time = 0, value = red } });
            }
            WithFx(avatar, toggle);

            MobileBuild.Override = true;
            var errors = BesidesSlots(Build(avatar));

            Assert.That(Reports(errors), Has.All.EqualTo((ErrorSeverity.NonFatal, (string?)Messages.Excluded)));
            Assert.That(body.sharedMaterials.Select(m => m.shader.name), Has.All.EqualTo(ToonStandardName));
            Assert.That(body.sharedMaterials.Distinct().Count(), Is.EqualTo(3));
            var clip = FxClips(avatar).Single();
            var swaps = AnimationUtility.GetObjectReferenceCurveBindings(clip)
                .Select(binding => (binding.propertyName, AnimationUtility.GetObjectReferenceCurve(clip, binding)[0].value.name))
                .Where(swap => !swap.propertyName.Contains("-1")).OrderBy(swap => swap.propertyName);
            Assert.That(swaps, Is.EqualTo(new[]
                { ("m_Materials.Array.data[1]", "Red eyes (UV Wizard)"), ("m_Materials.Array.data[2]", "Red mouth (UV Wizard)") }));
        }
        [Test]
        public void ALaterPassThatEditsTheFxControllerKeepsItsEdits()
        {
            // Avatar Optimizer edits the descriptor's controllers directly, after the bake. The bake's animator
            // services must have written their controllers back by then, or they'd overwrite those edits at the end.
            var avatar = Avatar(withComponent: true);
            var hatOff = Keep(new AnimationClip { name = "Hat off" });
            hatOff.SetCurve("Hat", typeof(GameObject), "m_IsActive", AnimationCurve.Constant(0, 1, 0));
            WithFx(avatar, hatOff);

            MobileBuild.Override = true;
            DirectFxEditPlugin.Enabled = true;
            try { Build(avatar); }
            finally { DirectFxEditPlugin.Enabled = false; }

            var clip = FxClips(avatar).Single();
            Assert.That(AnimationUtility.GetCurveBindings(clip).Select(binding => binding.path), Has.Member(DirectFxEditPlugin.Path));
        }

        [Test]
        public void ASwapInAChildAnimatorExcludesTheSlotItSwaps()
        {
            // The hat has its own Animator, whose clip paths start at the hat: "" is the hat itself.
            var avatar = Avatar(withComponent: true);
            var hat = avatar.transform.Find("Hat");
            hat.GetComponent<MeshRenderer>().sharedMaterial.name = "Hat blue";
            var swap = Keep(new AnimationClip { name = "Hat recolour" });
            AnimationUtility.SetObjectReferenceCurve(swap,
                EditorCurveBinding.PPtrCurve("", typeof(MeshRenderer), "m_Materials.Array.data[0]"),
                new[] { new ObjectReferenceKeyframe { time = 0, value = Standard(Gradient(16, 6)) } });
            WithAnimator(hat.gameObject, swap);

            MobileBuild.Override = true;
            var errors = Build(avatar);

            // The slot is excluded and gets its own converted material. Had the swap been missed, the hat would merge,
            // and then its Animator would put the source material back when NDMF commits the controllers: an
            // Animator rebinds the materials its clips swap whenever its controller changes.
            Assert.That(hat.GetComponent<MeshRenderer>().sharedMaterial.name, Is.EqualTo("Hat blue (UV Wizard)"));
            Assert.That(Texts(errors), Has.Some.Contain("Hat blue").And.Contain("Hat recolour"));
            Assert.That(SwappedIn(hat.GetComponent<Animator>().runtimeAnimatorController.animationClips.Single()).Select(m => m.shader.name), Is.EqualTo(new[] { ToonStandardName }),
                "a child animator's swap is converted too");
        }

        [Test]
        public void AClipSharedByAChildAnimatorAndFxSwapsWhatEachOfThemFinds()
        {
            // The FX controller and an Animator on the hat play the same clip. Its path "" names the hat from the
            // hat's Animator, and the avatar root from FX; its path "Tail" names the tail from FX, and nothing from
            // the hat. NDMF clones each controller in a scope of its own, so each gets its own copy of the clip; if
            // they ever shared one, only one of the two ways to resolve its paths would be kept.
            var avatar = Avatar(withComponent: true);
            var hat = avatar.transform.Find("Hat");
            hat.GetComponent<MeshRenderer>().sharedMaterial.name = "Hat blue";
            var tail = avatar.transform.Find("Tail").GetComponent<SkinnedMeshRenderer>();
            tail.sharedMaterial.name = "Tail blue";
            var swap = Keep(new AnimationClip { name = "Recolour" });
            AnimationUtility.SetObjectReferenceCurve(swap,
                EditorCurveBinding.PPtrCurve("", typeof(MeshRenderer), "m_Materials.Array.data[0]"),
                new[] { new ObjectReferenceKeyframe { time = 0, value = Standard(Gradient(16, 6)) } });
            AnimationUtility.SetObjectReferenceCurve(swap, Slot("Tail", 0),
                new[] { new ObjectReferenceKeyframe { time = 0, value = Standard(Gradient(16, 7)) } });
            WithAnimator(hat.gameObject, swap);
            WithFx(avatar, swap);

            MobileBuild.Override = true;
            var errors = BesidesSlots(Build(avatar));

            Assert.That(hat.GetComponent<MeshRenderer>().sharedMaterial.name, Is.EqualTo("Hat blue (UV Wizard)"));
            Assert.That(tail.sharedMaterial.name, Is.EqualTo("Tail blue (UV Wizard)"));
            Assert.That(Texts(errors), Has.Some.Contain("Hat blue").And.Some.Contain("Tail blue"));
        }

        [Test]
        public void ASwapInAChildAnimatorFollowsItsSlotWhenItMoves()
        {
            // The hat's first two slots merge into one, so the child Animator's swap on slot 2 moves to slot 1.
            var avatar = Avatar(withComponent: true);
            var hat = avatar.transform.Find("Hat");
            hat.GetComponent<MeshFilter>().sharedMesh = Mesh(new[] { Quad(2, 0.05f, 0.4f, 0.55f, 0.95f) },
                new[] { Quad(4, 0.1f, 0.1f, 0.3f, 0.3f) }, new[] { Quad(5, 0.6f, 0.6f, 0.8f, 0.8f) });
            var feather = Standard(Gradient(16, 5));
            feather.name = "Feather";
            hat.GetComponent<MeshRenderer>().sharedMaterials = new[] { Standard(Gradient(16, 2)), Standard(Gradient(16, 4)), feather };
            var red = Standard(Gradient(16, 6));
            red.name = "Red";
            var swap = Keep(new AnimationClip { name = "Red feather" });
            AnimationUtility.SetObjectReferenceCurve(swap,
                EditorCurveBinding.PPtrCurve("", typeof(MeshRenderer), "m_Materials.Array.data[2]"),
                new[] { new ObjectReferenceKeyframe { time = 0, value = red } });
            var animator = WithAnimator(hat.gameObject, swap);

            MobileBuild.Override = true;
            Build(avatar);

            Assert.That(hat.GetComponent<MeshRenderer>().sharedMaterials.Select(m => m.name).Skip(1), Is.EqualTo(new[] { "Feather (UV Wizard)" }));
            var clip = animator.runtimeAnimatorController.animationClips.Single();
            Assert.That(AnimationUtility.GetObjectReferenceCurveBindings(clip).Select(binding => (binding.propertyName,
                    AnimationUtility.GetObjectReferenceCurve(clip, binding)[0].value.name)),
                Is.EqualTo(new[] { ("m_Materials.Array.data[1]", "Red (UV Wizard)") }));
        }

        [Test]
        public void ASwapOnASlotPastTheRenderersMaterialsStaysWhereItIs()
        {
            // The body's first two slots merge, so the swapped eyes move from slot 2 to slot 1. The clip's curve on
            // slot 5, which the body doesn't have, animates nothing, and is left as it is.
            var avatar = Avatar(withComponent: true);
            var body = avatar.transform.Find("Body").GetComponent<SkinnedMeshRenderer>();
            body.sharedMesh = Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }, new[] { Quad(1, 0.2f, 0.3f, 0.9f, 0.8f) },
                new[] { Quad(4, 0.1f, 0.1f, 0.3f, 0.3f) });
            body.sharedMaterials = new[] { body.sharedMaterials[0], body.sharedMaterials[1], Standard(Gradient(16, 4)) };
            var toggle = Keep(new AnimationClip { name = "Red eyes on" });
            foreach (var slot in new[] { 2, 5 })
                AnimationUtility.SetObjectReferenceCurve(toggle, Slot("Body", slot),
                    new[] { new ObjectReferenceKeyframe { time = 0, value = Standard(Gradient(16, 6)) } });
            WithFx(avatar, toggle);

            MobileBuild.Override = true;
            Build(avatar);

            Assert.That(FxClips(avatar).SelectMany(AnimationUtility.GetObjectReferenceCurveBindings)
                    .Select(binding => binding.propertyName).OrderBy(name => name),
                Is.EqualTo(new[] { "m_Materials.Array.data[1]", "m_Materials.Array.data[5]" }));
        }

        [Test]
        public void AMobileBuildLeavesEveryMaterialOnAShaderMobileAllowsAndExcludedOnesKeepTheirColours()
        {
            // The hat repeats its tinted texture past the repeat limit, and an FX toggle swaps the tail between two
            // Standard materials, so neither merges.
            var avatar = Avatar(withComponent: true);
            var hat = avatar.transform.Find("Hat");
            hat.GetComponent<MeshFilter>().sharedMesh = Mesh(new[] { Quad(2, 0, 0, 5, 1) });
            var tinted = Standard(Gradient(16, 2));
            tinted.color = new Color(1, 0.6f, 0.3f);
            hat.GetComponent<MeshRenderer>().sharedMaterials = new[] { tinted };
            var before = Centroids(hat.gameObject).Select(SourceColour).ToArray();
            var spots = Standard(Gradient(16, 5));
            var toggle = Keep(new AnimationClip { name = "Spotted tail" });
            AnimationUtility.SetObjectReferenceCurve(toggle,
                EditorCurveBinding.PPtrCurve("Tail", typeof(SkinnedMeshRenderer), "m_Materials.Array.data[0]"),
                new[] { new ObjectReferenceKeyframe { time = 0, value = spots } });
            WithFx(avatar, toggle);

            MobileBuild.Override = true;
            var errors = BesidesSlots(Build(avatar));

            Assert.That(Reports(errors), Has.All.EqualTo((ErrorSeverity.NonFatal, (string?)Messages.Excluded)));
            var shaders = Materials(avatar).Concat(FxClips(avatar).SelectMany(SwappedIn)).Select(m => m.shader.name);
            Assert.That(shaders, Has.All.Matches<string>(name => ToonStandard.MobileShaders.Contains(name)));
            var converted = hat.GetComponent<MeshRenderer>().sharedMaterial;
            var albedo = Readable((Texture2D)converted.mainTexture);
            var after = Centroids(hat.gameObject).Select(c => albedo.GetPixelBilinear(c.Uv.x, c.Uv.y) * converted.color).ToArray();
            for (var k = 0; k < before.Length; k++)
            for (var channel = 0; channel < 3; channel++)
                Assert.That(after[k][channel], Is.EqualTo(before[k][channel]).Within(0.08f), $"triangle {k}, channel {channel}");
        }

        [Test]
        public void TheMobileShadersAreTheSdksShaderWhitelist()
        {
            // The SDK's VRC.SDKBase assembly isn't one this assembly references, so its validation is found by name.
            var validation = System.AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("VRC.SDKBase.Validation.AvatarValidation")).FirstOrDefault(type => type != null);
            var whitelist = validation?.GetField("ShaderWhiteList",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            Assume.That(whitelist, Is.Not.Null, "the SDK keeps its whitelist where 3.10.5 did");

            Assert.That(ToonStandard.MobileShaders, Is.EquivalentTo((IEnumerable<string>)whitelist!.GetValue(null)));
        }

        [Test]
        public void ASwapOntoALineKeepsItsSourceAndTheLineIsReported()
        {
            // One clip swaps the same Standard material onto the tail, a mesh, and onto a leash line, which keeps its
            // material as the bake leaves particles and lines alone.
            var avatar = Avatar(withComponent: true);
            var leash = new GameObject("Leash").AddComponent<LineRenderer>();
            leash.transform.SetParent(avatar.transform);
            leash.sharedMaterials = new[] { Standard(Gradient(16, 6)) };
            var spots = Standard(Gradient(16, 5));
            var toggle = Keep(new AnimationClip { name = "Spotted" });
            AnimationUtility.SetObjectReferenceCurve(toggle,
                EditorCurveBinding.PPtrCurve("Tail", typeof(SkinnedMeshRenderer), "m_Materials.Array.data[0]"),
                new[] { new ObjectReferenceKeyframe { time = 0, value = spots } });
            AnimationUtility.SetObjectReferenceCurve(toggle,
                EditorCurveBinding.PPtrCurve("Leash", typeof(LineRenderer), "m_Materials.Array.data[0]"),
                new[] { new ObjectReferenceKeyframe { time = 0, value = spots } });
            WithFx(avatar, toggle);

            MobileBuild.Override = true;
            var errors = BesidesSlots(Build(avatar));

            var clip = FxClips(avatar).Single();
            Material SwapOn(string path) => (Material)AnimationUtility.GetObjectReferenceCurve(clip,
                AnimationUtility.GetObjectReferenceCurveBindings(clip).Single(binding => binding.path == path))[0].value;
            Assert.That(SwapOn("Tail").shader.name, Is.EqualTo(ToonStandardName));
            Assert.That(SwapOn("Leash").shader.name, Is.EqualTo("Standard"));
            Assert.That(leash.sharedMaterial.shader.name, Is.EqualTo("Standard"));
            Assert.That(Reports(errors), Has.Member((ErrorSeverity.NonFatal, (string?)Messages.Unconverted)));
        }

        // Asserts every renderer but the body is on Toon Standard.
        static void AssertTheRestAreOnToonStandard(GameObject avatar)
        {
            foreach (var other in new[] { "BodyCopy", "Hat", "Tail" })
                Assert.That(avatar.transform.Find(other).GetComponent<Renderer>().sharedMaterials.Select(m => m.shader.name),
                    Has.All.EqualTo(ToonStandardName), other);
        }

        // The materials a clip's material swaps put in their slots.
        static IEnumerable<Material> SwappedIn(AnimationClip clip) => AnimationUtility.GetObjectReferenceCurveBindings(clip)
            .SelectMany(binding => AnimationUtility.GetObjectReferenceCurve(clip, binding))
            .Select(key => key.value).OfType<Material>();

        static EditorCurveBinding Slot(string path, int slot) =>
            EditorCurveBinding.PPtrCurve(path, typeof(SkinnedMeshRenderer), $"m_Materials.Array.data[{slot}]");

        // Gives the object an Animator of its own, whose controller has one state playing the clip.
        Animator WithAnimator(GameObject target, AnimationClip clip)
        {
            var controller = Keep(new AnimatorController { name = target.name });
            controller.AddLayer("Recolour");
            controller.layers[0].stateMachine.AddState(clip.name).motion = clip;
            var animator = target.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            return animator;
        }

        // Gives the avatar an FX controller with one layer holding a state for each clip.
        void WithFx(GameObject avatar, params AnimationClip[] clips)
        {
            var controller = Keep(new AnimatorController { name = "FX" });
            controller.AddLayer("Toggles");
            foreach (var clip in clips) controller.layers[0].stateMachine.AddState(clip.name).motion = clip;
            var descriptor = avatar.GetComponent<VRCAvatarDescriptor>();
            descriptor.customizeAnimationLayers = true;
            // Only the FX layer: a default layer would need the SDK's fallback controllers, which a test can't rely on.
            descriptor.baseAnimationLayers = new[]
            {
                new VRCAvatarDescriptor.CustomAnimLayer
                {
                    type = VRCAvatarDescriptor.AnimLayerType.FX, isEnabled = true, animatorController = controller,
                },
            };
            descriptor.specialAnimationLayers = new VRCAvatarDescriptor.CustomAnimLayer[0];
        }

        // The clips of the avatar's FX controller as the build left it.
        static IEnumerable<AnimationClip> FxClips(GameObject avatar)
        {
            var fx = avatar.GetComponent<VRCAvatarDescriptor>().baseAnimationLayers
                .Single(layer => layer.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;
            return fx.animationClips;
        }

        static IEnumerable<(ErrorSeverity, string?)> Reports(List<ErrorContext> errors) =>
            errors.Select(e => (e.TheError.Severity, (e.TheError as SimpleError)?.TitleKey)).ToArray();

        // Leaves out the material-slot warning, for avatars whose meshes nothing merges: the SDK counts a slot on each,
        // more than a one-slot preset such as Good allows.
        static List<ErrorContext> BesidesSlots(List<ErrorContext> errors) =>
            errors.Where(e => (e.TheError as SimpleError)?.TitleKey != Messages.TooManySlots).ToList();

        static List<string> Texts(List<ErrorContext> errors) =>
            errors.Select(e => (e.TheError as SimpleError)?.ToMessage() ?? "").ToList();

        Material ToonStandardMaterial(Texture texture, float culling)
        {
            var material = Keep(new Material(Shader.Find(ToonStandardName)) { mainTexture = texture });
            material.SetFloat("_Culling", culling);
            return material;
        }

        // The texture sampled at the UV tiled the given number of times.
        static Color TiledSample(Texture2D texture, Vector2 uv, float tiling) => texture.GetPixelBilinear(uv.x * tiling, uv.y * tiling);

        // Processes the avatar in place, on the ambient platform, which is VRChat's in a VRChat project.
        static List<ErrorContext> Build(GameObject avatar) =>
            ErrorReport.CaptureErrors(() => AvatarProcessor.ProcessAvatar(avatar));

        // An avatar root with a skinned body of two submeshes, a hat and a skinned tail, on Standard materials. It
        // doesn't add Trace and Optimize, whose own optimizations these tests don't cover.
        GameObject Avatar(bool withComponent)
        {
            var root = Keep(new GameObject("Avatar"));
            root.AddComponent<VRCAvatarDescriptor>();
            if (withComponent) root.AddComponent<UVWizardAvatar>().addTraceAndOptimize = false;

            var body = new GameObject("Body").AddComponent<SkinnedMeshRenderer>();
            body.transform.SetParent(root.transform);
            body.sharedMesh = Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }, new[] { Quad(1, 0.2f, 0.3f, 0.9f, 0.8f) });
            body.sharedMaterials = new[] { Standard(Gradient(16, 0)), Standard(Gradient(32, 1)) };

            // A second skinned renderer drawing the same mesh, as an outfit variant might.
            var copy = new GameObject("BodyCopy").AddComponent<SkinnedMeshRenderer>();
            copy.transform.SetParent(root.transform);
            copy.sharedMesh = body.sharedMesh;
            copy.sharedMaterials = body.sharedMaterials;

            var hat = new GameObject("Hat");
            hat.transform.SetParent(root.transform);
            hat.AddComponent<MeshFilter>().sharedMesh = Mesh(new[] { Quad(2, 0.05f, 0.4f, 0.55f, 0.95f) });
            hat.AddComponent<MeshRenderer>().sharedMaterials = new[] { Standard(Gradient(16, 2)) };

            // A second skinned mesh of its own, so two distinct skinned meshes bake together.
            var tail = new GameObject("Tail").AddComponent<SkinnedMeshRenderer>();
            tail.transform.SetParent(root.transform);
            tail.sharedMesh = Mesh(new[] { Quad(3, 0.6f, 0.05f, 0.95f, 0.35f) });
            tail.sharedMaterials = new[] { Standard(Gradient(16, 3)) };
            return root;
        }

        static Mesh?[] Meshes(GameObject avatar) => avatar.GetComponentsInChildren<Renderer>().Select(r => r switch
        {
            SkinnedMeshRenderer skinned => skinned.sharedMesh,
            _ => r.GetComponent<MeshFilter>().sharedMesh,
        }).ToArray();

        static Material[] Materials(GameObject avatar) =>
            avatar.GetComponentsInChildren<Renderer>().SelectMany(r => r.sharedMaterials).ToArray();

        Material Standard(Texture2D texture) => Keep(new Material(Shader.Find("Standard")) { mainTexture = texture });

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
