#nullable enable

using NUnit.Framework;
using UnityEngine;

namespace UVWizard.Core.Tests
{
    public class BakeFixtureTests : BakeFixture
    {
        [Test]
        public void NormalMappedEnablesStandardsKeyword()
        {
            var material = Keep(new Material(Shader.Find("Standard")));

            NormalMapped(material, NormalGradient(4, 0), 1);

            Assert.That(material.IsKeywordEnabled("_NORMALMAP"), Is.True);
        }

        [Test]
        public void NormalMappedRejectsAShaderWhoseKeywordItDoesNotKnow()
        {
            var material = Keep(new Material(Shader.Find("Unlit/Texture")));

            Assert.That(() => NormalMapped(material, NormalGradient(4, 0), 1),
                Throws.ArgumentException.With.Message.Contains("Unlit/Texture"));
        }
    }
}
