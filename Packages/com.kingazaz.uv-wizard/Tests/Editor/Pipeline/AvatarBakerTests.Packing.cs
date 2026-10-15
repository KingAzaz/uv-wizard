#nullable enable

using NUnit.Framework;
using UnityEngine;
using UVWizard.Core.Packing;
using UVWizard.Core.Pipeline;

namespace UVWizard.Core.Tests.Pipeline
{
    // What the report (#24) says about the packing: which packer placed the charts, and the detail kept.
    public partial class AvatarBakerTests
    {
        [Test]
        public void ABakeThatFitsAtFullDetailSaysTheExactPackerKeptItAll()
        {
            var result = Bake(MeshRenderer(Mesh(new[] { Quad(0, 0.1f, 0.1f, 0.6f, 0.7f) }),
                Standard(Gradient(64, 0), Color.white)));

            Assert.That(result.Packing, Is.Not.Null);
            Assert.That(result.Packing!.Value.Packer, Is.EqualTo(Packer.Exact));
            Assert.That(result.Packing.Value.Scale, Is.EqualTo(1));
            Assert.That(result.Packing.Value.RanOutOfTime, Is.False);
        }

        [Test]
        public void ABakeIntoAnAtlasSmallerThanItsTexturesKeepsLessDetail()
        {
            // A 256 px texture spread over the whole UV square can't keep its detail in a 128 px atlas.
            var result = Bake(StandardOptions(atlasSize: 128, padding: 2),
                MeshRenderer(Mesh(new[] { Quad(0, 0.05f, 0.05f, 0.95f, 0.95f) }), Standard(Gradient(256, 0), Color.white)));

            Assert.That(result.Fits, Is.True);
            Assert.That(result.Packing, Is.Not.Null);
            Assert.That(result.Packing!.Value.Scale, Is.GreaterThan(0.25f).And.LessThan(0.6f));
        }

        [Test]
        public void ACachedBakeSaysHowTheFreshOneWasPacked()
        {
            var options = StandardOptions(atlasSize: 128, padding: 2, cacheDirectory: CacheDirectory);
            Renderer Wide() => MeshRenderer(Mesh(new[] { Quad(0, 0.05f, 0.05f, 0.95f, 0.95f) }),
                Standard(Gradient(256, 0), Color.white));
            var fresh = Bake(options, Wide());

            var cached = Bake(options, Wide());

            Assert.That(cached.FromCache, Is.True);
            Assert.That(fresh.Packing, Is.Not.Null);
            Assert.That(cached.Packing, Is.EqualTo(fresh.Packing));
        }

        [Test]
        public void ABakeThatMergesNothingHasNoPacking()
        {
            var result = Bake(StandardOptions());

            Assert.That(result.Packing, Is.Null);
        }
    }
}
