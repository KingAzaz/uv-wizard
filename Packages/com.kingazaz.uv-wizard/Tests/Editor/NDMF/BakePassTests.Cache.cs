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
            BakeAnother();

            var second = BakeAnother();

            Assert.That(second!.FromCache, Is.True);
            Assert.That(Directory.GetFiles("Library/UVWizardCache", "*.bake"), Is.Not.Empty);
        }
    }
}
