using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UVWizard.Core.Baking;

namespace UVWizard.Core.Tests.Baking
{
    public class NormalMapsTests
    {
        const string Folder = "Assets/UVWizardNormalMapsTests";

        [TearDown]
        public void DeleteAssets() => AssetDatabase.DeleteAsset(Folder);

        [TestCase(BuildTargetGroup.Android)]
        [TestCase(BuildTargetGroup.iOS)]
        public void MobileSetToDxt5nmStyleIsAg(BuildTargetGroup group) =>
            Assert.That(NormalMaps.EncodingFor(group, NormalMapEncoding.DXT5nm), Is.EqualTo(NormalEncoding.AG));

        [Test]
        public void MobileSetToXyzIsRgOrAg() =>
            Assert.That(NormalMaps.EncodingFor(BuildTargetGroup.Android, NormalMapEncoding.XYZ),
                Is.EqualTo(NormalEncoding.RGorAG));

        [Test]
        public void DesktopIsRgOrAg() =>
            Assert.That(NormalMaps.EncodingFor(BuildTargetGroup.Standalone, NormalMapEncoding.DXT5nm),
                Is.EqualTo(NormalEncoding.RGorAG));

        [Test]
        public void ATextureImportedAsANormalMapIsInThePlatformsEncoding()
        {
            var texture = Imported(TextureImporterType.NormalMap);

            Assert.That(NormalMaps.EncodingOf(texture, NormalEncoding.AG), Is.EqualTo(NormalEncoding.AG));
        }

        [Test]
        public void ATextureImportedAsAnythingElseIsReadAsItIs()
        {
            var texture = Imported(TextureImporterType.Default);

            Assert.That(NormalMaps.EncodingOf(texture, NormalEncoding.AG), Is.EqualTo(NormalEncoding.RGorAG));
        }

        // A 4x4 PNG imported into a temporary folder as the given texture type.
        static Texture2D Imported(TextureImporterType type)
        {
            Directory.CreateDirectory(Folder);
            var path = Folder + "/normal.png";
            var source = new Texture2D(4, 4, TextureFormat.RGBA32, mipChain: false, linear: true);
            File.WriteAllBytes(path, source.EncodeToPNG());
            Object.DestroyImmediate(source);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = type;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
