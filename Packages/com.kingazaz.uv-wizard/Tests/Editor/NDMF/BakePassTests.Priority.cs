#nullable enable

using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using NUnit.Framework;
using UnityEngine;
using UVWizard.Core.Pipeline;

namespace UVWizard.NDMF.Tests
{
    // Head priority (#23): the pass finds the head through the avatar's humanoid rig.
    public partial class BakePassTests
    {
        [Test]
        public void OnAHumanoidAvatarChartsOnTheHeadKeepMoreDetail()
        {
            var root = Keep(new GameObject("Avatar"));
            var head = Humanoid(root);
            var (glasses, necklace) = Accessories(head, head.parent);

            BakePass.Bake(root, new BakeOptions(Shader.Find(ToonStandardName), atlasSize: 512, padding: 0));

            Assert.That(Width(glasses) / Width(necklace), Is.EqualTo(AvatarBaker.HeadPriority).Within(0.05f));
        }

        [Test]
        public void AnAvatarWithoutAHumanoidRigBakesWithoutPriorityOrErrors()
        {
            var root = Keep(new GameObject("Avatar"));
            root.AddComponent<Animator>();
            var hips = new GameObject("Hips").transform;
            hips.SetParent(root.transform);
            var head = new GameObject("Head").transform;
            head.SetParent(hips);
            var (glasses, badge) = Accessories(head, hips);

            var errors = ErrorReport.CaptureErrors(() =>
                BakePass.Bake(root, new BakeOptions(Shader.Find(ToonStandardName), atlasSize: 512, padding: 0)));

            Assert.That(errors, Is.Empty);
            Assert.That(Width(glasses) / Width(badge), Is.EqualTo(1).Within(0.05f));
        }

        // Glasses on the head and another accessory on the body bone given, each a rigid mesh of one equal quad on a
        // 1024 px texture. At 512 px they can't both fit at full detail.
        (MeshRenderer Glasses, MeshRenderer Other) Accessories(Transform head, Transform body)
        {
            MeshRenderer Accessory(string name, Transform parent, float uMin, int seed)
            {
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = Mesh(new[] { Quad(0, uMin, uMin, uMin + 0.4f, uMin + 0.4f) });
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = new[] { Standard(Gradient(1024, seed)) };
                return renderer;
            }
            return (Accessory("Glasses", head, 0.05f, 0), Accessory("Other", body, 0.55f, 1));
        }

        static float Width(MeshRenderer renderer)
        {
            var xs = renderer.GetComponent<MeshFilter>().sharedMesh.uv.Select(uv => uv.x).ToArray();
            return xs.Max() - xs.Min();
        }

        // Gives the root the smallest humanoid skeleton Unity accepts, and an Animator with its avatar. Returns the head.
        Transform Humanoid(GameObject root)
        {
            Transform Bone(HumanBodyBones bone, Transform parent, Vector3 local)
            {
                var t = new GameObject(bone.ToString()).transform;
                t.SetParent(parent, false);
                t.localPosition = local;
                return t;
            }
            var hips = Bone(HumanBodyBones.Hips, root.transform, new Vector3(0, 1, 0));
            var spine = Bone(HumanBodyBones.Spine, hips, new Vector3(0, 0.1f, 0));
            var chest = Bone(HumanBodyBones.Chest, spine, new Vector3(0, 0.15f, 0));
            var neck = Bone(HumanBodyBones.Neck, chest, new Vector3(0, 0.2f, 0));
            var head = Bone(HumanBodyBones.Head, neck, new Vector3(0, 0.1f, 0));
            foreach (var left in new[] { true, false })
            {
                var x = left ? -1 : 1;
                var upperLeg = Bone(left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg, hips, new Vector3(0.1f * x, -0.05f, 0));
                var lowerLeg = Bone(left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg, upperLeg, new Vector3(0, -0.45f, 0));
                Bone(left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot, lowerLeg, new Vector3(0, -0.45f, 0.05f));
                var upperArm = Bone(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm, chest, new Vector3(0.2f * x, 0.15f, 0));
                var lowerArm = Bone(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm, upperArm, new Vector3(0.3f * x, 0, 0));
                Bone(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand, lowerArm, new Vector3(0.25f * x, 0, 0));
            }

            var human = new List<HumanBone>();
            var skeleton = new List<SkeletonBone>();
            foreach (var t in root.GetComponentsInChildren<Transform>())
            {
                skeleton.Add(new SkeletonBone { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale });
                if (t == root.transform) continue;
                var bone = new HumanBone { boneName = t.name, humanName = HumanTrait.BoneName[(int)System.Enum.Parse(typeof(HumanBodyBones), t.name)] };
                bone.limit.useDefaultValues = true;
                human.Add(bone);
            }
            var avatar = Keep(AvatarBuilder.BuildHumanAvatar(root, new HumanDescription
            {
                human = human.ToArray(), skeleton = skeleton.ToArray(), upperArmTwist = 0.5f, lowerArmTwist = 0.5f,
                upperLegTwist = 0.5f, lowerLegTwist = 0.5f, armStretch = 0.05f, legStretch = 0.05f,
            }));
            root.AddComponent<Animator>().avatar = avatar;
            return head;
        }
    }
}
