#nullable enable

using System.Linq;
using nadena.dev.ndmf;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UVWizard.NDMF.Tests
{
    // The report's links after an upload build (#98): the build's clone is gone, so a listed renderer, or a material
    // the build copied, links to the original avatar's.
    public partial class BakePassTests
    {
        [Test]
        public void AfterABuildOfACloneClickingAListedRendererSelectsTheOriginalAvatarsRenderer()
        {
            var (_, hat, _) = AvatarWithFlaggedHat();
            var clone = UploadClone(hat.transform.root.gameObject);

            BuildAndDestroy(clone);

            var row = BakeReports.Latest!.Materials.Single();
            BakeReportWindow.Select(BakeReports.Link(row.Renderers.Single()));
            Assert.That(Selection.activeObject, Is.SameAs(hat));
        }

        [Test]
        public void ARendererWithNoOriginalKeepsItsNameAndADisabledLink()
        {
            var (_, hat, _) = AvatarWithFlaggedHat();
            var clone = UploadClone(hat.transform.root.gameObject);
            hat.name = "Old hat";

            BuildAndDestroy(clone);

            var row = BakeReports.Latest!.Materials.Single();
            Assert.That(BakeReports.Link(row.Renderers.Single()) == null);
            Assert.That(row.RendererNames.Single(), Is.EqualTo("Hat"));
        }

        // As Modular Avatar does when it swaps a material: the build's copy is no use once the build ends.
        [Test]
        public void AMaterialTheBuildCopiedLinksToTheOriginalRenderersMaterial()
        {
            var (_, hat, doubleSided) = AvatarWithFlaggedHat();
            var clone = UploadClone(hat.transform.root.gameObject);
            var copy = Keep(new Material(doubleSided));
            clone.transform.Find("Hat").GetComponent<Renderer>().sharedMaterials = new[] { copy };

            BuildAndDestroy(clone);

            var row = BakeReports.Latest!.Materials.Single();
            Assert.That(row.Material, Is.SameAs(copy));
            Assert.That(BakeReports.Link(row.Material), Is.SameAs(doubleSided));
        }

        [Test]
        public void AMaterialTheBuildRegisteredAsACopyLinksToWhatItCopied()
        {
            var (_, hat, doubleSided) = AvatarWithFlaggedHat();
            var clone = UploadClone(hat.transform.root.gameObject);
            var source = Keep(new Material(doubleSided) { name = "Source" });
            var copy = Keep(new Material(doubleSided));
            clone.transform.Find("Hat").GetComponent<Renderer>().sharedMaterials = new[] { copy };

            // NDMF keeps an outer registry for the build, as it does for a build inside another.
            using (new ObjectRegistryScope(new ObjectRegistry(clone.transform)))
            {
                ObjectRegistry.RegisterReplacedObject(source, copy);
                BuildAndDestroy(clone);
            }

            Assert.That(BakeReports.Link(BakeReports.Latest!.Materials.Single().Material), Is.SameAs(source));
        }

        // As Modular Avatar does when it moves an object: NDMF's registry keeps where the build first saw it.
        [Test]
        public void ARendererTheBuildMovedLinksToTheOriginalAvatarsRenderer()
        {
            var (_, hat, _) = AvatarWithFlaggedHat();
            var clone = UploadClone(hat.transform.root.gameObject);
            var cloneHat = clone.transform.Find("Hat");

            using (new ObjectRegistryScope(new ObjectRegistry(clone.transform)))
            {
                ObjectRegistry.GetReference(cloneHat.GetComponent<Renderer>());
                cloneHat.SetParent(clone.transform.Find("Body"));
                BuildAndDestroy(clone);
            }

            Assert.That(BakeReports.Link(BakeReports.Latest!.Materials.Single().Renderers.Single()), Is.SameAs(hat));
        }

        // An asset outlives the build, whatever the original renderer holds.
        [Test]
        public void AnAssetMaterialLinksToItself()
        {
            var (_, hat, doubleSided) = AvatarWithFlaggedHat();
            var clone = UploadClone(hat.transform.root.gameObject);
            var folder = AssetDatabase.GUIDToAssetPath(AssetDatabase.CreateFolder("Assets", "UVWizardReportLinkTests"));
            try
            {
                var asset = new Material(doubleSided);
                AssetDatabase.CreateAsset(asset, folder + "/Hat.mat");
                clone.transform.Find("Hat").GetComponent<Renderer>().sharedMaterials = new[] { asset };

                BuildAndDestroy(clone);

                Assert.That(BakeReports.Link(BakeReports.Latest!.Materials.Single().Material), Is.SameAs(asset));
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
            }
        }

        // A manual bake keeps its clone in the scene, and the creator looks at the baked result.
        [Test]
        public void AfterAManualBakeAListedRendererLinksToTheBakedClone()
        {
            var (avatar, _, _) = AvatarWithFlaggedHat();
            BakeReports.Clear();

            var clone = Keep(ManualBake.Run(avatar, BuildTarget.Android));

            var row = BakeReports.Latest!.Materials.Single();
            Assert.That(BakeReports.Link(row.Renderers.Single()), Is.SameAs(clone.transform.Find("Hat").GetComponent<Renderer>()));
        }

        [Test]
        public void AnAvatarBuiltInPlaceLinksToItsOwnRenderers()
        {
            var (avatar, hat, doubleSided) = AvatarWithFlaggedHat();
            BakeReports.Clear();

            MobileBuild.Override = true;
            Build(avatar);

            var row = BakeReports.Latest!.Materials.Single();
            Assert.That(BakeReports.Link(row.Renderers.Single()), Is.SameAs(hat));
            Assert.That(BakeReports.Link(row.Material), Is.SameAs(doubleSided));
        }

        // As in AMobileBuildReportsEachFlaggedMaterialOnceBesideItsErrorReport: the double-sided hat is flagged.
        (GameObject avatar, MeshRenderer hat, Material doubleSided) AvatarWithFlaggedHat()
        {
            var avatar = Avatar(withComponent: true);
            var body = avatar.transform.Find("Body").GetComponent<SkinnedMeshRenderer>();
            body.sharedMaterials = body.sharedMaterials.Select(source => ToonStandardMaterial(source.mainTexture, culling: 2)).ToArray();
            var hat = avatar.transform.Find("Hat").GetComponent<MeshRenderer>();
            var doubleSided = ToonStandardMaterial(Gradient(16, 4), culling: 0);
            hat.sharedMaterials = new[] { doubleSided };
            return (avatar, hat, doubleSided);
        }

        // The VRChat SDK uploads a clone of the scene avatar, named as Instantiate names it.
        GameObject UploadClone(GameObject avatar)
        {
            var clone = Keep(Object.Instantiate(avatar));
            Assert.That(clone.name, Is.EqualTo(avatar.name + "(Clone)"));
            return clone;
        }

        // As an upload does: NDMF destroys the clone once the build ends.
        void BuildAndDestroy(GameObject clone)
        {
            BakeReports.Clear();
            MobileBuild.Override = true;
            Build(clone);
            Object.DestroyImmediate(clone);
        }
    }
}
