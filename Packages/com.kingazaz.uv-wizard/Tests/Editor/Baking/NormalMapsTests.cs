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

        [Test]
        public void MobileSetToXyzIsRgOrAg() =>
            Assert.That(NormalMaps.EncodingFor(BuildTargetGroup.Android, NormalMapEncoding.XYZ),
                Is.EqualTo(NormalEncoding.RGorAG));

        [Test]
        public void DesktopIsRgOrAg() =>
            Assert.That(NormalMaps.EncodingFor(BuildTargetGroup.Standalone, NormalMapEncoding.DXT5nm),
                Is.EqualTo(NormalEncoding.RGorAG));
    }
}
