using NUnit.Framework;
using UnityEditor;
using UVWizard.Core.Baking;

namespace UVWizard.Core.Tests.Baking
{
    public class NormalMapsTests
    {
        [TestCase(BuildTargetGroup.Android)]
        [TestCase(BuildTargetGroup.iOS)]
        public void MobileSetToDxt5nmStyleIsAg(BuildTargetGroup group) =>
            Assert.That(NormalMaps.EncodingFor(group, NormalMapEncoding.DXT5nm), Is.EqualTo(NormalEncoding.AG));

        [TestCase(BuildTargetGroup.Android)]
        [TestCase(BuildTargetGroup.iOS)]
        public void MobileSetToXyzIsXyz(BuildTargetGroup group) =>
            Assert.That(NormalMaps.EncodingFor(group, NormalMapEncoding.XYZ), Is.EqualTo(NormalEncoding.XYZ));

        [Test]
        public void DesktopSetToXyzIsRgOrAg() =>
            Assert.That(NormalMaps.EncodingFor(BuildTargetGroup.Standalone, NormalMapEncoding.XYZ),
                Is.EqualTo(NormalEncoding.RGorAG));

        [Test]
        public void DesktopIsRgOrAg() =>
            Assert.That(NormalMaps.EncodingFor(BuildTargetGroup.Standalone, NormalMapEncoding.DXT5nm),
                Is.EqualTo(NormalEncoding.RGorAG));
    }
}
