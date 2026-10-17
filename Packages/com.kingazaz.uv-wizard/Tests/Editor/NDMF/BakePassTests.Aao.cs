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
            var errors = BesidesSlots(Build(avatar));

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
            var errors = BesidesSlots(Build(avatar));

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
            var apart = AddUnmergeable(avatar, extra);

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
            var mask = QuadrantMask();
            var removal = body.gameObject.AddComponent<RemoveMeshByMask>();
            removal.Initialize(1);
            removal.Materials = new[]
            {
                new RemoveMeshByMask.MaterialSlot { Enabled = true, Mask = mask, Mode = RemoveMeshByMask.RemoveMode.RemoveWhite },
                new RemoveMeshByMask.MaterialSlot { Enabled = false },
            };

            MobileBuild.Override = true;
            var errors = BesidesSlots(Build(avatar));

            Assert.That(OwnReports(errors), Is.Empty);
            var mesh = body.sharedMesh;
            var positions = mesh.vertices;
            var drawn = Enumerable.Range(0, mesh.subMeshCount).SelectMany(s => mesh.GetIndices(s)).ToArray();
            Assert.That(DrawnZ(body), Is.EqualTo(new[] { 0f, 2f }),
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
        public void ASkinnedMeshWithRemoveMeshByMaskAndNoFreeUvChannelIsKeptOut()
        {
            // The mask reads the torso's UV0, but UV1 to UV7 are all the mesh's own, so the bake has nowhere to keep
            // the source UV0 for AAO to find.
            var avatar = MergeableAvatar(addTraceAndOptimize: false);
            var torso = avatar.transform.Find("Torso").GetComponent<SkinnedMeshRenderer>();
            var source = torso.sharedMesh;
            for (var channel = 1; channel < 8; channel++)
                source.SetUVs(channel, source.vertices.Select(p => new Vector2(p.z, channel)).ToList());
            var materials = torso.sharedMaterials;
            var removal = torso.gameObject.AddComponent<RemoveMeshByMask>();
            removal.Initialize(1);
            removal.Materials = new[]
            {
                new RemoveMeshByMask.MaterialSlot { Enabled = true, Mask = QuadrantMask(), Mode = RemoveMeshByMask.RemoveMode.RemoveWhite },
                new RemoveMeshByMask.MaterialSlot { Enabled = false },
            };

            MobileBuild.Override = true;
            var errors = BesidesSlots(Build(avatar));

            Assert.That(OwnReports(errors), Is.EqualTo(Enumerable.Repeat((ErrorSeverity.NonFatal, (string?)Messages.Excluded), 2)),
                "each of the torso's slots is excluded");
            Assert.That(Texts(errors), Has.All.Contain("needs this mesh's UVs where they are"));
            Assert.That(torso.sharedMaterials.Select(m => m.shader.name), Is.EqualTo(materials.Select(_ => ToonStandardName)));
        }

        [Test]
        public void RemoveMeshByMaskOnAMergeSkinnedMeshTargetDeletesTheRightTriangles()
        {
            // Merge Skinned Mesh merges the torso and tail into a new renderer whose mask, on its first slot, reads
            // the merged UV0. The torso's first slot has one quad under the mask's white and one under its black;
            // its second slot's quad lies under the white too, but the mask isn't on that slot.
            var avatar = MergeableAvatar(addTraceAndOptimize: false);
            var torso = avatar.transform.Find("Torso").GetComponent<SkinnedMeshRenderer>();
            var tail = avatar.transform.Find("Tail").GetComponent<SkinnedMeshRenderer>();
            torso.sharedMesh = Mesh(new[] { Quad(0, 0.05f, 0.05f, 0.45f, 0.45f), Quad(2, 0.55f, 0.55f, 0.95f, 0.95f) },
                new[] { Quad(5, 0.6f, 0.6f, 0.9f, 0.9f) });
            torso.sharedMesh.boneWeights = Enumerable.Repeat(new BoneWeight { boneIndex0 = 0, weight0 = 1 }, torso.sharedMesh.vertexCount).ToArray();
            torso.sharedMesh.bindposes = new[] { torso.bones[0].worldToLocalMatrix * torso.transform.localToWorldMatrix };
            var textures = SourceTextures(avatar);
            var target = MergeInto(avatar, "Merged", torso, tail);
            var mask = QuadrantMask();
            var removal = target.gameObject.AddComponent<RemoveMeshByMask>();
            removal.Initialize(1);
            removal.Materials = new[]
            {
                new RemoveMeshByMask.MaterialSlot { Enabled = true, Mask = mask, Mode = RemoveMeshByMask.RemoveMode.RemoveWhite },
                new RemoveMeshByMask.MaterialSlot { Enabled = false },
                new RemoveMeshByMask.MaterialSlot { Enabled = false },
            };

            var warnings = new List<string>();
            void Collect(string message, string _, LogType type)
            {
                if (type == LogType.Warning) warnings.Add(message);
            }
            MobileBuild.Override = true;
            Application.logMessageReceived += Collect;
            List<ErrorContext> errors;
            try { errors = BesidesSlots(Build(avatar)); }
            finally { Application.logMessageReceived -= Collect; }

            Assert.That(warnings, Has.None.Contains("before initialization"));
            Assert.That(Debug.unityLogger.logEnabled, Is.True, "logging is back on");
            // AAO also reports that Remove Mesh By Mask doesn't support a Merge Skinned Mesh target.
            Assert.That(OwnReports(errors), Has.Exactly(3).EqualTo((ErrorSeverity.NonFatal, (string?)Messages.Excluded)),
                "each source slot is excluded");
            Assert.That(Texts(errors), Has.Exactly(3).Contains("merges it into Merged"), "each note names the merge target");
            Assert.That(Texts(errors), Has.None.Contains("needs this mesh's UVs"), "no note gives the generic reason");
            Assert.That(DrawnZ(target), Is.EqualTo(new[] { 0f, 3f, 5f }),
                "the mask deleted the first slot's quad under its white, and only it");
            AssertKeepsColours(target, textures);
        }

        [TestCase(false, TestName = "ADisabledSourceThatAaoWontMergeIntoATargetThatReadsUvsIsBaked")]
        [TestCase(true, TestName = "AnInactiveSourceThatAaoWontMergeIntoATargetThatReadsUvsIsBaked")]
        public void ASourceThatAaoWontMergeIntoATargetThatReadsUvsIsBaked(bool objectOff)
        {
            // With Skip Enablement Mismatched Renderers on, AAO leaves the tail, turned off by its renderer or its
            // object, out of the enabled target, so the tail stays a renderer of its own, which no Remove Mesh
            // component reads.
            var avatar = MergeableAvatar(addTraceAndOptimize: false);
            var torso = avatar.transform.Find("Torso").GetComponent<SkinnedMeshRenderer>();
            var tail = avatar.transform.Find("Tail").GetComponent<SkinnedMeshRenderer>();
            var torsoSlots = torso.sharedMaterials.Length;
            var tailMaterials = tail.sharedMaterials;
            var textures = SourceTextures(avatar);
            if (objectOff) tail.gameObject.SetActive(false);
            else tail.enabled = false;
            var target = MergeInto(avatar, "Merged", torso, tail);
            var merge = new SerializedObject(target.GetComponent<MergeSkinnedMesh>());
            merge.FindProperty("skipEnablementMismatchedRenderers").boolValue = true;
            merge.ApplyModifiedPropertiesWithoutUndo();
            var removal = target.gameObject.AddComponent<RemoveMeshByMask>();
            removal.Initialize(1);
            removal.Materials = new[]
            {
                new RemoveMeshByMask.MaterialSlot { Enabled = true, Mask = QuadrantMask(), Mode = RemoveMeshByMask.RemoveMode.RemoveWhite },
                new RemoveMeshByMask.MaterialSlot { Enabled = false },
            };

            MobileBuild.Override = true;
            var errors = BesidesSlots(Build(avatar));

            Assert.That(OwnReports(errors), Has.Exactly(torsoSlots).EqualTo((ErrorSeverity.NonFatal, (string?)Messages.Excluded)),
                "only the torso's slots are excluded");
            Assert.That(tail != null, "AAO kept the tail apart");
            Assert.That(tail!.sharedMaterials.Intersect(tailMaterials), Is.Empty, "the tail is on the merged material");
            AssertKeepsColours(tail, textures);
        }

        [Test]
        public void TheSourcesOfAMergeSkinnedMeshTargetThatReadsNoUvsAreBaked()
        {
            var avatar = MergeableAvatar(addTraceAndOptimize: false);
            var textures = SourceTextures(avatar);
            var target = MergeInto(avatar, "Merged", avatar.transform.Find("Torso").GetComponent<SkinnedMeshRenderer>(),
                avatar.transform.Find("Tail").GetComponent<SkinnedMeshRenderer>());

            MobileBuild.Override = true;
            var errors = Build(avatar);

            Assert.That(OwnReports(errors), Has.None.EqualTo((ErrorSeverity.NonFatal, (string?)Messages.Excluded)));
            Assert.That(target.sharedMaterials.Distinct().Count(), Is.EqualTo(1), "every slot is on the merged material");
            AssertKeepsColours(target, textures);
        }

        [Test]
        public void RemoveMeshByMaskOnTheOuterTargetOfNestedMergesDeletesTheRightTriangles()
        {
            // The torso and tail merge into Inner, and Inner into Outer, whose mask reads the merged UV0. The
            // torso's first slot has one quad under the mask's white and one under its black.
            var avatar = MergeableAvatar(addTraceAndOptimize: false);
            var torso = avatar.transform.Find("Torso").GetComponent<SkinnedMeshRenderer>();
            torso.sharedMesh = Mesh(new[] { Quad(0, 0.05f, 0.05f, 0.45f, 0.45f), Quad(2, 0.55f, 0.55f, 0.95f, 0.95f) },
                new[] { Quad(5, 0.6f, 0.6f, 0.9f, 0.9f) });
            torso.sharedMesh.boneWeights = Enumerable.Repeat(new BoneWeight { boneIndex0 = 0, weight0 = 1 }, torso.sharedMesh.vertexCount).ToArray();
            torso.sharedMesh.bindposes = new[] { torso.bones[0].worldToLocalMatrix * torso.transform.localToWorldMatrix };
            var inner = MergeInto(avatar, "Inner", torso, avatar.transform.Find("Tail").GetComponent<SkinnedMeshRenderer>());
            var outer = MergeInto(avatar, "Outer", inner);
            var removal = outer.gameObject.AddComponent<RemoveMeshByMask>();
            removal.Initialize(1);
            removal.Materials = new[]
            {
                new RemoveMeshByMask.MaterialSlot { Enabled = true, Mask = QuadrantMask(), Mode = RemoveMeshByMask.RemoveMode.RemoveWhite },
                new RemoveMeshByMask.MaterialSlot { Enabled = false },
                new RemoveMeshByMask.MaterialSlot { Enabled = false },
            };

            MobileBuild.Override = true;
            var errors = BesidesSlots(Build(avatar));

            Assert.That(OwnReports(errors), Has.Exactly(3).EqualTo((ErrorSeverity.NonFatal, (string?)Messages.Excluded)));
            Assert.That(Texts(errors), Has.Exactly(3).Contains("merges it into Outer"), "each note names the target that reads the UVs");
            Assert.That(DrawnZ(outer), Is.EqualTo(new[] { 0f, 3f, 5f }),
                "the mask deleted the first slot's quad under its white, and only it");
        }

        [Test]
        public void RemoveMeshByUvTileOnAMergeSkinnedMeshTargetStillDeletesPerMaterialSlot()
        {
            // Every quad lies in UV1's tile 1, but only the target's first slot deletes that tile. Baked sources
            // would share the merged material, and Merge Skinned Mesh would merge their slots into one.
            var avatar = MergeableAvatar(addTraceAndOptimize: false);
            var torso = avatar.transform.Find("Torso").GetComponent<SkinnedMeshRenderer>();
            var tail = avatar.transform.Find("Tail").GetComponent<SkinnedMeshRenderer>();
            foreach (var source in new[] { torso, tail })
                source.sharedMesh.uv2 = Enumerable.Repeat(new Vector2(1.5f, 0.5f), source.sharedMesh.vertexCount).ToArray();
            var target = MergeInto(avatar, "Merged", torso, tail);
            var tiles = new SerializedObject(target.gameObject.AddComponent(AaoMeshEdits.RemoveMeshByUVTile!));
            var slots = tiles.FindProperty("materials");
            slots.arraySize = 3;
            for (var s = 0; s < 3; s++)
            {
                slots.GetArrayElementAtIndex(s).FindPropertyRelative("uvChannel").enumValueIndex = 1;
                slots.GetArrayElementAtIndex(s).FindPropertyRelative("removeTile1").boolValue = s == 0;
            }
            tiles.ApplyModifiedPropertiesWithoutUndo();

            MobileBuild.Override = true;
            var errors = BesidesSlots(Build(avatar));

            Assert.That(OwnReports(errors), Has.Exactly(3).EqualTo((ErrorSeverity.NonFatal, (string?)Messages.Excluded)));
            Assert.That(DrawnZ(target), Is.EqualTo(new[] { 1f, 3f }),
                "only the first slot's quad is deleted");
        }

        [Test]
        public void RemoveMeshByMaskOnAMergeSkinnedMeshTargetDeletesTheRightTrianglesOfAStaticSource()
        {
            // The glasses have one quad under the mask's white and one under its black.
            var avatar = MergeableAvatar(addTraceAndOptimize: false);
            var glasses = new GameObject("Glasses");
            glasses.transform.SetParent(avatar.transform);
            glasses.AddComponent<MeshFilter>().sharedMesh = Mesh(new[] { Quad(5, 0.55f, 0.55f, 0.95f, 0.95f), Quad(6, 0.05f, 0.05f, 0.45f, 0.45f) });
            var renderer = glasses.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { Standard(Gradient(16, 5)) };
            var target = MergeInto(avatar, "Merged", renderer);
            var mask = QuadrantMask();
            var removal = target.gameObject.AddComponent<RemoveMeshByMask>();
            removal.Initialize(1);
            removal.Materials = new[] { new RemoveMeshByMask.MaterialSlot { Enabled = true, Mask = mask, Mode = RemoveMeshByMask.RemoveMode.RemoveWhite } };

            MobileBuild.Override = true;
            var errors = BesidesSlots(Build(avatar));

            Assert.That(OwnReports(errors), Has.Exactly(1).EqualTo((ErrorSeverity.NonFatal, (string?)Messages.Excluded)));
            Assert.That(DrawnZ(target), Is.EqualTo(new[] { 6f }),
                "the mask deleted the quad under its white, and only it");
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
            var mask = QuadrantMask();
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
            Assert.That(DrawnZ(renderer), Is.EqualTo(new[] { 6f, 7f }),
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
            glasses.AddComponent(AaoMeshEdits.RemoveMeshByUVTile!);

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
            var type = AaoMeshEdits.RemoveMeshByUVTile!;
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
            var errors = BesidesSlots(Build(avatar));

            Assert.That(OwnReports(errors), Is.Empty);
            Assert.That(DrawnZ(torso), Is.EqualTo(new[] { 1f }), "only the first slot's quad is deleted");
        }

        // The depths of the quads the renderer's mesh still draws. Quad puts every quad at its own depth.
        static float[] DrawnZ(Renderer renderer)
        {
            var mesh = renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh : renderer.GetComponent<MeshFilter>().sharedMesh;
            var positions = mesh.vertices;
            return Enumerable.Range(0, mesh.subMeshCount).SelectMany(s => mesh.GetIndices(s)).Select(i => positions[i].z)
                .Distinct().OrderBy(z => z).ToArray();
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

        // Every triangle the renderer draws samples the colour its source sampled at its centroid, from the
        // atlas or from the texture of a slot the bake left out.
        void AssertKeepsColours(SkinnedMeshRenderer renderer, Dictionary<float, Texture2D> textures)
        {
            var mesh = renderer.sharedMesh;
            var positions = mesh.vertices;
            var uvs = mesh.uv;
            for (var s = 0; s < mesh.subMeshCount; s++)
            {
                var atlas = Readable((Texture2D)renderer.sharedMaterials[s].mainTexture);
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

        // Adds a renderer Trace and Optimize won't merge with the rest, and returns it.
        Renderer AddUnmergeable(GameObject avatar, Unmergeable extra)
        {
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
            return apart;
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

        // A Merge Skinned Mesh target of the sources, set up by its fields as the inspector does, so AAO warns if
        // its API is read before Initialize.
        static SkinnedMeshRenderer MergeInto(GameObject avatar, string name, params Renderer[] sources)
        {
            var merged = new GameObject(name);
            merged.transform.SetParent(avatar.transform);
            var target = merged.AddComponent<SkinnedMeshRenderer>();
            var merge = new SerializedObject(merged.AddComponent<MergeSkinnedMesh>());
            void Set(string set, Renderer[] renderers)
            {
                var property = merge.FindProperty(set + ".mainSet");
                property.arraySize = renderers.Length;
                for (var r = 0; r < renderers.Length; r++) property.GetArrayElementAtIndex(r).objectReferenceValue = renderers[r];
            }
            Set("renderersSet", sources.OfType<SkinnedMeshRenderer>().ToArray<Renderer>());
            Set("staticRenderersSet", sources.OfType<MeshRenderer>().ToArray<Renderer>());
            merge.ApplyModifiedPropertiesWithoutUndo();
            return target;
        }

        // A 16 px mask, white in the quarter above 0.5 in both U and V, black elsewhere.
        Texture2D QuadrantMask()
        {
            var mask = Keep(new Texture2D(16, 16, TextureFormat.RGBA32, mipChain: false));
            mask.SetPixels32(Enumerable.Range(0, 256).Select(i => i % 16 >= 8 && i / 16 >= 8 ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 255)).ToArray());
            mask.Apply();
            return mask;
        }

        // An FX layer that turns the named children on and off together.
        void Toggle(GameObject avatar, params string[] children) =>
            Animate(avatar, typeof(GameObject), "m_IsActive", children);

        // An FX layer that turns the named children's renderers on and off together, leaving their objects on.
        void ToggleRenderers(GameObject avatar, params string[] children) =>
            Animate(avatar, typeof(SkinnedMeshRenderer), "m_Enabled", children);

        void Animate(GameObject avatar, Type type, string property, params string[] children)
        {
            var clip = Keep(new AnimationClip());
            foreach (var child in children)
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(child, type, property),
                    new AnimationCurve(new Keyframe(0, 1), new Keyframe(1, 0)));
            var controller = Keep(new AnimatorController());
            controller.AddParameter(children[0], AnimatorControllerParameterType.Bool);
            controller.AddLayer(children[0]);
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
