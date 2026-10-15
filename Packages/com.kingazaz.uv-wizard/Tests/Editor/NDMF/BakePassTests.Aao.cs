#nullable enable

#if UVWIZARD_AAO
using System;
using System.Collections.Generic;
using System.Linq;
using Anatawa12.AvatarOptimizer;
using nadena.dev.ndmf;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;
using VRC.SDK3.Avatars.Components;

namespace UVWizard.NDMF.Tests
{
    // Full builds through Avatar Optimizer, which ADR 0001 relies on for the one skinned mesh.
    public partial class BakePassTests
    {
        [Test]
        public void WithoutTraceAndOptimizeTheBuildAddsOneAndEndsWithOneSkinnedMeshAndOneSlot()
        {
            var avatar = MergeableAvatar(addTraceAndOptimize: true);
            var textures = SourceTextures(avatar);

            MobileBuild.Override = true;
            var errors = Build(avatar);

            Assert.That(OwnReports(errors), Is.EqualTo(new[] { (ErrorSeverity.Information, (string?)Messages.AddedTraceAndOptimize) }));
            var renderers = avatar.GetComponentsInChildren<Renderer>(true);
            Assert.That(renderers, Has.Length.EqualTo(1));
            Assert.That(renderers[0], Is.InstanceOf<SkinnedMeshRenderer>());
            Assert.That(renderers[0].sharedMaterials, Has.Length.EqualTo(1));
            // Trace and Optimize's texture optimization repacks the atlas into less space, but it looks the same.
            AssertKeepsColours((SkinnedMeshRenderer)renderers[0], textures);
        }

        [Test]
        public void WithTheSettingOffNoTraceAndOptimizeIsAdded()
        {
            var avatar = MergeableAvatar(addTraceAndOptimize: false);

            MobileBuild.Override = true;
            var errors = Build(avatar);

            Assert.That(OwnReports(errors), Is.Empty);
            Assert.That(avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true), Has.Length.EqualTo(2));
        }

        [Test]
        public void APcBuildAddsNoTraceAndOptimize()
        {
            var avatar = MergeableAvatar(addTraceAndOptimize: true);

            MobileBuild.Override = false;
            var errors = Build(avatar);

            Assert.That(OwnReports(errors), Is.Empty);
            Assert.That(avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true), Has.Length.EqualTo(2));
        }

        [Test]
        public void AnAvatarWithItsOwnTraceAndOptimizeKeepsIt()
        {
            // The avatar's own Trace and Optimize doesn't merge meshes, and stays the one AAO reads.
            var avatar = MergeableAvatar(addTraceAndOptimize: true);
            var own = new SerializedObject(avatar.AddComponent<TraceAndOptimize>());
            own.FindProperty("mergeSkinnedMesh").boolValue = false;
            own.ApplyModifiedPropertiesWithoutUndo();

            MobileBuild.Override = true;
            var errors = Build(avatar);

            Assert.That(OwnReports(errors), Is.Empty);
            Assert.That(avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true), Has.Length.EqualTo(2));
        }

        // MmdBody: Trace and Optimize's MMD world compatibility, on by default, keeps a mesh named Body directly
        // under the root apart, since MMD worlds animate its blend shapes.
        public enum Unmergeable { ToggledOutfit, StaticMesh, OtherRootBone, MmdBody }

        // What Trace and Optimize won't merge, so one skinned mesh can't be promised: the torso and tail still
        // merge, and the extra renderer stays apart.
        [TestCase(Unmergeable.ToggledOutfit)]
        [TestCase(Unmergeable.StaticMesh)]
        [TestCase(Unmergeable.OtherRootBone)]
        [TestCase(Unmergeable.MmdBody)]
        public void TraceAndOptimizeLeavesApart(Unmergeable extra)
        {
            var avatar = MergeableAvatar(addTraceAndOptimize: true);
            var hips = avatar.transform.Find("Armature/Hips");
            Renderer apart;
            switch (extra)
            {
                case Unmergeable.ToggledOutfit:
                    apart = Skinned(avatar, "Outfit", hips, Mesh(new[] { Quad(4, 0.1f, 0.5f, 0.4f, 0.9f) }), Standard(Gradient(16, 4)));
                    Toggle(avatar, "Outfit");
                    break;
                case Unmergeable.StaticMesh:
                    var glasses = new GameObject("Glasses");
                    glasses.transform.SetParent(avatar.transform);
                    glasses.AddComponent<MeshFilter>().sharedMesh = Mesh(new[] { Quad(4, 0.1f, 0.5f, 0.4f, 0.9f) });
                    apart = glasses.AddComponent<MeshRenderer>();
                    apart.sharedMaterials = new[] { Standard(Gradient(16, 4)) };
                    break;
                case Unmergeable.MmdBody:
                    apart = Skinned(avatar, "Body", hips, Mesh(new[] { Quad(4, 0.1f, 0.5f, 0.4f, 0.9f) }), Standard(Gradient(16, 4)));
                    break;
                default:
                    var head = new GameObject("Head").transform;
                    head.SetParent(hips);
                    head.localPosition = Vector3.up;
                    apart = Skinned(avatar, "Hair", head, Mesh(new[] { Quad(4, 0.1f, 0.5f, 0.4f, 0.9f) }), Standard(Gradient(16, 4)));
                    break;
            }

            MobileBuild.Override = true;
            Build(avatar);

            var renderers = avatar.GetComponentsInChildren<Renderer>(true);
            Assert.That(renderers, Has.Length.EqualTo(2));
            Assert.That(renderers, Has.Member(apart));
        }

        [Test]
        public void TrianglesRemoveMeshByMaskDeletesTakeNoAtlasSpaceAndTheRestKeepTheirColours()
        {
            // The torso's first slot holds two big quads on a 4096 px texture; the mask deletes the second. The
            // second slot's quad lies under the mask's white too, but the mask isn't on that slot. Both big quads
            // would only fit the 2048 atlas at about half scale; the first alone fits at full scale.
            var avatar = MergeableAvatar(addTraceAndOptimize: false);
            var body = avatar.transform.Find("Torso").GetComponent<SkinnedMeshRenderer>();
            var big = Gradient(4096, 0);
            var small = Gradient(16, 1);
            body.sharedMesh = Mesh(new[] { Quad(0, 0.05f, 0.05f, 0.45f, 0.45f), Quad(1, 0.55f, 0.55f, 0.95f, 0.95f) },
                new[] { Quad(2, 0.6f, 0.6f, 0.9f, 0.9f) });
            body.sharedMesh.boneWeights = Enumerable.Repeat(new BoneWeight { boneIndex0 = 0, weight0 = 1 }, body.sharedMesh.vertexCount).ToArray();
            body.sharedMesh.bindposes = new[] { body.bones[0].worldToLocalMatrix * body.transform.localToWorldMatrix };
            // The mesh's own UV1, which the source UV0 mustn't overwrite.
            body.sharedMesh.uv2 = body.sharedMesh.vertices.Select(p => new Vector2(p.z, 7)).ToArray();
            body.sharedMaterials = new[] { Standard(big), Standard(small) };
            var textures = SourceTextures(avatar);
            var mask = Keep(new Texture2D(16, 16, TextureFormat.RGBA32, mipChain: false));
            mask.SetPixels32(Enumerable.Range(0, 256).Select(i => i % 16 >= 8 && i / 16 >= 8 ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 255)).ToArray());
            mask.Apply();
            var removal = body.gameObject.AddComponent<RemoveMeshByMask>();
            removal.Initialize(1);
            removal.Materials = new[]
            {
                new RemoveMeshByMask.MaterialSlot { Enabled = true, Mask = mask, Mode = RemoveMeshByMask.RemoveMode.RemoveWhite },
                new RemoveMeshByMask.MaterialSlot { Enabled = false },
            };

            MobileBuild.Override = true;
            var errors = Build(avatar);

            Assert.That(OwnReports(errors), Is.Empty);
            var mesh = body.sharedMesh;
            var positions = mesh.vertices;
            var drawn = Enumerable.Range(0, mesh.subMeshCount).SelectMany(s => mesh.GetIndices(s)).ToArray();
            Assert.That(drawn.Select(i => positions[i].z).Distinct().OrderBy(z => z), Is.EqualTo(new[] { 0f, 2f }),
                "the mask deleted the second quad, and only it");
            Assert.That(mesh.HasVertexAttribute(VertexAttribute.TexCoord2), Is.False, "AAO dropped the kept source UVs");
            var uv1 = mesh.uv2;
            Assert.That(drawn.Select(i => uv1[i]), Has.All.Matches<Vector2>(uv => uv.y == 7), "the mesh's own UV1 stays");

            var uvs = mesh.uv;
            var first = drawn.Where(i => positions[i].z == 0).Select(i => uvs[i].x).ToArray();
            Assert.That(first.Max() - first.Min(), Is.GreaterThan(0.7f), "the deleted quad left the first room for full scale");
            AssertKeepsColours(body, textures);
        }

        [Test]
        public void AStaticMeshWithRemoveMeshByMaskIsKeptOutAndTheMaskDeletesTheRightTriangles()
        {
            // AAO runs Remove Mesh By Mask on a MeshRenderer too, but can't be told where the bake keeps its
            // source UVs. The glasses' first slot has one quad under the mask's white and one under its black;
            // the second slot's quad lies under the white too, but the mask isn't on that slot.
            var avatar = MergeableAvatar(addTraceAndOptimize: false);
            var glasses = new GameObject("Glasses");
            glasses.transform.SetParent(avatar.transform);
            glasses.AddComponent<MeshFilter>().sharedMesh = Mesh(new[] { Quad(5, 0.55f, 0.55f, 0.95f, 0.95f), Quad(6, 0.05f, 0.05f, 0.45f, 0.45f) },
                new[] { Quad(7, 0.6f, 0.6f, 0.9f, 0.9f) });
            var renderer = glasses.AddComponent<MeshRenderer>();
            var materials = new[] { Standard(Gradient(16, 5)), Standard(Gradient(16, 7)) };
            renderer.sharedMaterials = materials;
            var mask = Keep(new Texture2D(16, 16, TextureFormat.RGBA32, mipChain: false));
            mask.SetPixels32(Enumerable.Range(0, 256).Select(i => i % 16 >= 8 && i / 16 >= 8 ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 255)).ToArray());
            mask.Apply();
            var removal = glasses.AddComponent<RemoveMeshByMask>();
            removal.Initialize(1);
            removal.Materials = new[]
            {
                new RemoveMeshByMask.MaterialSlot { Enabled = true, Mask = mask, Mode = RemoveMeshByMask.RemoveMode.RemoveWhite },
                new RemoveMeshByMask.MaterialSlot { Enabled = false },
            };

            MobileBuild.Override = true;
            var errors = BesidesSlots(Build(avatar));

            Assert.That(OwnReports(errors), Is.EqualTo(Enumerable.Repeat((ErrorSeverity.NonFatal, (string?)Messages.Excluded), 2)));
            // Each keeps a Toon Standard material of its own.
            Assert.That(renderer.sharedMaterials.Select(m => m.shader.name), Is.EqualTo(materials.Select(_ => ToonStandardName)));
            Assert.That(renderer.sharedMaterials.Distinct().Count(), Is.EqualTo(materials.Length));
            var mesh = glasses.GetComponent<MeshFilter>().sharedMesh;
            var positions = mesh.vertices;
            var drawn = Enumerable.Range(0, mesh.subMeshCount).SelectMany(s => mesh.GetIndices(s));
            Assert.That(drawn.Select(i => positions[i].z).Distinct().OrderBy(z => z), Is.EqualTo(new[] { 6f, 7f }),
                "the mask deleted the first slot's quad under its white, and only it");
        }

        [Test]
        public void AStaticMeshWithRemoveMeshByUvTileIsKeptOut()
        {
            var avatar = MergeableAvatar(addTraceAndOptimize: false);
            var glasses = new GameObject("Glasses");
            glasses.transform.SetParent(avatar.transform);
            glasses.AddComponent<MeshFilter>().sharedMesh = Mesh(new[] { Quad(5, 0.55f, 0.55f, 0.95f, 0.95f) });
            var renderer = glasses.AddComponent<MeshRenderer>();
            var materials = new[] { Standard(Gradient(16, 5)) };
            renderer.sharedMaterials = materials;
            glasses.AddComponent(typeof(TraceAndOptimize).Assembly.GetType("Anatawa12.AvatarOptimizer.RemoveMeshByUVTile"));

            MobileBuild.Override = true;
            var errors = BesidesSlots(Build(avatar));

            Assert.That(OwnReports(errors), Is.EqualTo(new[] { (ErrorSeverity.NonFatal, (string?)Messages.Excluded) }));
            // Each keeps a Toon Standard material of its own.
            Assert.That(renderer.sharedMaterials.Select(m => m.shader.name), Is.EqualTo(materials.Select(_ => ToonStandardName)));
            Assert.That(renderer.sharedMaterials.Distinct().Count(), Is.EqualTo(materials.Length));
        }

        [Test]
        public void RemoveMeshByUvTileStillDeletesPerMaterialSlot()
        {
            // Both of the torso's slots have their quad in UV1's tile 1, but only the first slot's setting deletes
            // that tile. Remove Mesh By UV Tile isn't in AAO's public API, so the test sets it up by its fields.
            var avatar = MergeableAvatar(addTraceAndOptimize: false);
            var torso = avatar.transform.Find("Torso").GetComponent<SkinnedMeshRenderer>();
            torso.sharedMesh.uv2 = Enumerable.Repeat(new Vector2(1.5f, 0.5f), torso.sharedMesh.vertexCount).ToArray();
            var type = typeof(TraceAndOptimize).Assembly.GetType("Anatawa12.AvatarOptimizer.RemoveMeshByUVTile");
            var serialized = new SerializedObject(torso.gameObject.AddComponent(type));
            var slots = serialized.FindProperty("materials");
            slots.arraySize = 2;
            for (var s = 0; s < 2; s++)
            {
                slots.GetArrayElementAtIndex(s).FindPropertyRelative("uvChannel").enumValueIndex = 1;
                slots.GetArrayElementAtIndex(s).FindPropertyRelative("removeTile1").boolValue = s == 0;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();

            MobileBuild.Override = true;
            var errors = Build(avatar);

            Assert.That(OwnReports(errors), Is.Empty);
            var mesh = torso.sharedMesh;
            var positions = mesh.vertices;
            var drawn = Enumerable.Range(0, mesh.subMeshCount).SelectMany(s => mesh.GetIndices(s));
            Assert.That(drawn.Select(i => positions[i].z).Distinct(), Is.EqualTo(new[] { 1f }), "only the first slot's quad is deleted");
        }

        // Each quad's source texture, by its depth: Quad puts every quad at its own depth, at positions equal to
        // its source UVs.
        static Dictionary<float, Texture2D> SourceTextures(GameObject avatar)
        {
            var textures = new Dictionary<float, Texture2D>();
            foreach (var renderer in avatar.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var positions = renderer.sharedMesh.vertices;
                for (var s = 0; s < renderer.sharedMesh.subMeshCount; s++)
                    foreach (var i in renderer.sharedMesh.GetIndices(s))
                        textures[positions[i].z] = (Texture2D)renderer.sharedMaterials[s].mainTexture;
            }
            return textures;
        }

        // Every triangle the renderer draws samples the colour its source sampled at its centroid.
        void AssertKeepsColours(SkinnedMeshRenderer renderer, Dictionary<float, Texture2D> textures)
        {
            var mesh = renderer.sharedMesh;
            var positions = mesh.vertices;
            var uvs = mesh.uv;
            var atlas = Readable((Texture2D)renderer.sharedMaterial.mainTexture);
            for (var s = 0; s < mesh.subMeshCount; s++)
            {
                var indices = mesh.GetIndices(s);
                for (var k = 0; k < indices.Length; k += 3)
                {
                    var corners = new[] { indices[k], indices[k + 1], indices[k + 2] };
                    var source = corners.Aggregate(Vector2.zero, (sum, i) => sum + (Vector2)positions[i]) / 3;
                    var atlasUv = corners.Aggregate(Vector2.zero, (sum, i) => sum + uvs[i]) / 3;
                    var expected = textures[positions[corners[0]].z].GetPixelBilinear(source.x, source.y);
                    var actual = atlas.GetPixelBilinear(atlasUv.x, atlasUv.y);
                    for (var channel = 0; channel < 3; channel++)
                        Assert.That(actual[channel], Is.EqualTo(expected[channel]).Within(0.08f),
                            $"submesh {s}, triangle {k / 3}, channel {channel}");
                }
            }
        }

        // A torso of two slots and a tail, skinned to the same root bone with the same bounds, so Trace and
        // Optimize can merge them. Not "Body": see MmdBody.
        GameObject MergeableAvatar(bool addTraceAndOptimize)
        {
            var root = Keep(new GameObject("Avatar"));
            root.AddComponent<Animator>();
            var descriptor = root.AddComponent<VRCAvatarDescriptor>();
            descriptor.baseAnimationLayers = Layers(VRCAvatarDescriptor.AnimLayerType.Base, VRCAvatarDescriptor.AnimLayerType.Additive,
                VRCAvatarDescriptor.AnimLayerType.Gesture, VRCAvatarDescriptor.AnimLayerType.Action, VRCAvatarDescriptor.AnimLayerType.FX);
            descriptor.specialAnimationLayers = Layers(VRCAvatarDescriptor.AnimLayerType.Sitting,
                VRCAvatarDescriptor.AnimLayerType.TPose, VRCAvatarDescriptor.AnimLayerType.IKPose);
            root.AddComponent<UVWizardAvatar>().addTraceAndOptimize = addTraceAndOptimize;
            var armature = new GameObject("Armature").transform;
            armature.SetParent(root.transform);
            var hips = new GameObject("Hips").transform;
            hips.SetParent(armature);

            Skinned(root, "Torso", hips, Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }, new[] { Quad(1, 0.2f, 0.3f, 0.9f, 0.8f) }),
                Standard(Gradient(16, 0)), Standard(Gradient(32, 1)));
            Skinned(root, "Tail", hips, Mesh(new[] { Quad(3, 0.6f, 0.05f, 0.95f, 0.35f) }), Standard(Gradient(16, 3)));
            return root;
        }

        static SkinnedMeshRenderer Skinned(GameObject avatar, string name, Transform bone, Mesh mesh, params Material[] materials)
        {
            var renderer = new GameObject(name).AddComponent<SkinnedMeshRenderer>();
            renderer.transform.SetParent(avatar.transform);
            mesh.boneWeights = Enumerable.Repeat(new BoneWeight { boneIndex0 = 0, weight0 = 1 }, mesh.vertexCount).ToArray();
            mesh.bindposes = new[] { bone.worldToLocalMatrix * renderer.transform.localToWorldMatrix };
            renderer.sharedMesh = mesh;
            renderer.bones = new[] { bone };
            renderer.rootBone = bone;
            renderer.probeAnchor = bone;
            renderer.localBounds = new Bounds(Vector3.zero, Vector3.one * 4);
            renderer.sharedMaterials = materials;
            return renderer;
        }

        // An FX layer that turns the named child on and off.
        void Toggle(GameObject avatar, string child)
        {
            var clip = Keep(new AnimationClip());
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(child, typeof(GameObject), "m_IsActive"),
                new AnimationCurve(new Keyframe(0, 1), new Keyframe(1, 0)));
            var controller = Keep(new AnimatorController());
            controller.AddParameter(child, AnimatorControllerParameterType.Bool);
            controller.AddLayer(child);
            controller.layers[0].stateMachine.AddState("Toggle").motion = clip;
            var descriptor = avatar.GetComponent<VRCAvatarDescriptor>();
            descriptor.customizeAnimationLayers = true;
            var fx = Array.FindIndex(descriptor.baseAnimationLayers, layer => layer.type == VRCAvatarDescriptor.AnimLayerType.FX);
            descriptor.baseAnimationLayers[fx].isDefault = false;
            descriptor.baseAnimationLayers[fx].animatorController = controller;
        }

        // What the build reported, less Avatar Optimizer's summary of what it optimized.
        static IEnumerable<(ErrorSeverity, string?)> OwnReports(List<ErrorContext> errors) =>
            Reports(errors).Where(report => report != (ErrorSeverity.Information, "OptimizationMetrics:Results")).ToArray();

        // The SDK's default layers, as the descriptor's inspector would set them up.
        static VRCAvatarDescriptor.CustomAnimLayer[] Layers(params VRCAvatarDescriptor.AnimLayerType[] types) =>
            types.Select(type => new VRCAvatarDescriptor.CustomAnimLayer { type = type, isEnabled = true, isDefault = true }).ToArray();
    }
}
#endif
