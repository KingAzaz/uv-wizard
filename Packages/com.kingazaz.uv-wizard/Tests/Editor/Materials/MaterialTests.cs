#nullable enable

using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UVWizard.Core.Tests.Materials
{
    /// <summary>Builds materials and textures in code, and destroys them after each test.</summary>
    public abstract class MaterialTests : ObjectFixture
    {
        // Colours round-trip through the project's colour space, which can move them by a rounding error.
        protected static void AssertColour(Color expected, Color actual, string? what = null,
            float tolerance = 1e-4f) =>
            Assert.That((Vector4)actual, Is.EqualTo((Vector4)expected).Using(new ApproximateVector4(tolerance)),
                what);

        sealed class ApproximateVector4 : IEqualityComparer<Vector4>
        {
            readonly float tolerance;

            public ApproximateVector4(float tolerance) => this.tolerance = tolerance;

            public bool Equals(Vector4 a, Vector4 b) => (a - b).magnitude < tolerance;
            public int GetHashCode(Vector4 v) => 0;
        }

        protected Material Material(string shaderName)
        {
            var shader = Shader.Find(shaderName);
            Assert.That(shader, Is.Not.Null, $"shader {shaderName}");
            return Keep(new Material(shader));
        }

        protected Texture2D Texture() => Keep(new Texture2D(4, 4));
    }
}
