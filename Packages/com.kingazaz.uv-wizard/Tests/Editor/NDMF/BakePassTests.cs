#nullable enable

using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;
using UVWizard.Core.Pipeline;
using VRC.SDK3.Avatars.Components;
using Object = UnityEngine.Object;

namespace UVWizard.NDMF.Tests
{
    // NDMF and the VRChat SDK aren't in CI's project, so these run only in the dev project.
    [Category("GPU")]
    public class BakePassTests
    {
        const string ToonStandardName = "VRChat/Mobile/Toon Standard";
        readonly List<Object> created = new List<Object>();

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
            var skinned = avatar.GetComponentsInChildren<SkinnedMeshRenderer>();
            var sources = skinned.Select(renderer => renderer.sharedMesh).ToArray();

            MobileBuild.Override = true;
            var errors = Build(avatar);

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
            var errors = Build(avatar);

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
            var errors = Build(avatar);

            Assert.That(Texts(errors), Is.Empty);
        }

        [Test]
        public void AnExcludedMaterialStaysAndIsReportedAsNonFatal()
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
            Assert.That(kept.shader.name, Is.EqualTo("Standard"));
            Assert.That(body.shader.name, Is.EqualTo(ToonStandardName));
            Assert.That(errors.Select(e => (e.TheError.Severity, (e.TheError as SimpleError)?.TitleKey)),
                Is.EqualTo(new[] { (ErrorSeverity.NonFatal, (string?)Messages.Excluded) }));
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
            var errors = Build(avatar);

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
            var errors = Build(avatar);

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
            var errors = Build(avatar);
            Assert.That(body.sharedMaterials.Select(m => m.shader.name), Is.EqualTo(new[] { ToonStandardName, "Standard" }));
            Assert.That(body.sharedMaterials[1].name, Is.EqualTo("Eyes blue"));
            foreach (var other in new[] { "BodyCopy", "Hat", "Tail" })
                Assert.That(avatar.transform.Find(other).GetComponent<Renderer>().sharedMaterials.Select(m => m.shader.name),
                    Has.All.EqualTo(ToonStandardName), other);
            Assert.That(Reports(errors), Has.All.EqualTo((ErrorSeverity.NonFatal, (string?)Messages.Excluded)));
            Assert.That(Texts(errors), Has.Count.EqualTo(2));
            Assert.That(Texts(errors), Has.Some.Contain("Eyes blue").And.Some.Contain("Eyes red"));
            Assert.That(Texts(errors), Has.All.Contain("Red eyes on"));
            var swaps = FxClips(avatar).SelectMany(clip => AnimationUtility.GetObjectReferenceCurveBindings(clip)
                .Select(binding => (binding.path, binding.propertyName,
                    AnimationUtility.GetObjectReferenceCurve(clip, binding)[0].value.name)));
            Assert.That(swaps, Is.EqualTo(new[] { ("Body", "m_Materials.Array.data[1]", "Eyes red") }));
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
            var errors = Build(avatar);

            var body = avatar.transform.Find("Body").GetComponent<SkinnedMeshRenderer>();
            Assert.That(body.sharedMaterials.Select(m => m.shader.name), Is.EqualTo(new[] { "Standard", "Standard" }));
            foreach (var other in new[] { "BodyCopy", "Hat", "Tail" })
                Assert.That(avatar.transform.Find(other).GetComponent<Renderer>().sharedMaterials.Select(m => m.shader.name),
                    Has.All.EqualTo(ToonStandardName), other);
            Assert.That(Reports(errors), Is.EqualTo(Enumerable.Repeat((ErrorSeverity.NonFatal, (string?)Messages.Excluded), 2)));
            Assert.That(Texts(errors), Has.All.Contain("Body glow").And.All.Contain("_EmissionColor"));
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
            var errors = Build(avatar);

            Assert.That(errors, Is.Empty);
            Assert.That(Materials(avatar).Select(m => m.shader.name), Has.All.EqualTo(ToonStandardName));
        }

        static EditorCurveBinding Slot(string path, int slot) =>
            EditorCurveBinding.PPtrCurve(path, typeof(SkinnedMeshRenderer), $"m_Materials.Array.data[{slot}]");

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

        static List<string> Texts(List<ErrorContext> errors) =>
            errors.Select(e => (e.TheError as SimpleError)?.ToMessage() ?? "").ToList();

        Material ToonStandardMaterial(Texture texture, float culling)
        {
            var material = Keep(new Material(Shader.Find(ToonStandardName)) { mainTexture = texture });
            material.SetFloat("_Culling", culling);
            return material;
        }

        // Processes the avatar in place, on the ambient platform, which is VRChat's in a VRChat project.
        static List<ErrorContext> Build(GameObject avatar) =>
            ErrorReport.CaptureErrors(() => AvatarProcessor.ProcessAvatar(avatar));

        // An avatar root with a skinned body of two submeshes, a hat and a skinned tail, on Standard materials.
        GameObject Avatar(bool withComponent)
        {
            var root = Keep(new GameObject("Avatar"));
            root.AddComponent<VRCAvatarDescriptor>();
            if (withComponent) root.AddComponent<UVWizardAvatar>();

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
