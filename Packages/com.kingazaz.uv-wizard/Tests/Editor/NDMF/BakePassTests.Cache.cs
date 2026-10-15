#nullable enable

using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace UVWizard.NDMF.Tests
{
    // Cached bakes (#22): NDMF re-runs the pass on every build and play-mode entry.
    public partial class BakePassTests
    {
        [Test]
        public void AMobileBuildCachesItsBakeInTheProjectsLibrary()
        {
            // An entry this build stores, or one it reads, which it marks as just used.
            var start = DateTime.UtcNow.AddSeconds(-1);
            var avatar = Avatar(withComponent: true);

            MobileBuild.Override = true;
            Build(avatar);

            var directory = new DirectoryInfo("Library/UVWizardCache");
            var used = directory.Exists ? directory.GetFiles("*.bake").Where(file => file.LastWriteTimeUtc >= start) : null;
            Assert.That(used, Is.Not.Null.And.Not.Empty);
        }
    }
}
