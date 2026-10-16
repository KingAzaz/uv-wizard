#nullable enable

using System.IO;
using NUnit.Framework;
using UnityEngine;
using UVWizard.Core.Pipeline;

namespace UVWizard.NDMF.Tests
{
    // Cached bakes (#22): NDMF re-runs the pass on every build and play-mode entry.
    public partial class BakePassTests
    {
        [Test]
        public void ASecondMobileBakeOfTheSameAvatarComesFromTheProjectsLibrary()
        {
            BakeResult? BakeAnother()
            {
                var avatar = Avatar(withComponent: true);
                return BakePass.Bake(avatar, ToonStandard.Options(Shader.Find(ToonStandardName), avatar.GetComponent<UVWizardAvatar>()));
            }
            // Entries left by earlier runs would let a bake that stores nothing pass.
            if (Directory.Exists(ToonStandard.CacheDirectory))
                foreach (var entry in Directory.GetFiles(ToonStandard.CacheDirectory, "*.bake")) File.Delete(entry);
            var first = BakeAnother();
            var stored = Directory.GetFiles(ToonStandard.CacheDirectory, "*.bake").Length;

            var second = BakeAnother();

            Assert.That(first!.FromCache, Is.False);
            Assert.That(stored, Is.GreaterThan(0), "this run's entries");
            Assert.That(second!.FromCache, Is.True);
        }
    }
}
