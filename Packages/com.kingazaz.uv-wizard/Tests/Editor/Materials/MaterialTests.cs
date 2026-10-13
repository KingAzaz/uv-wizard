#nullable enable

using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UVWizard.Core.Tests.Materials
{
    /// <summary>Builds materials and textures in code, and destroys them after each test.</summary>
    public abstract class MaterialTests
    {
        readonly List<Object> created = new List<Object>();

        [TearDown]
        public void DestroyCreated()
        {
            foreach (var o in created) Object.DestroyImmediate(o);
            created.Clear();
        }

        // Colours round-trip through the project's colour space, which can move them by a rounding error.
        protected static void AssertColour(Color expected, Color actual, string? what = null) =>
            Assert.That((Vector4)actual, Is.EqualTo((Vector4)expected).Using(Vector4Comparer), what);

        static readonly IEqualityComparer<Vector4> Vector4Comparer = new ApproximateVector4();

        sealed class ApproximateVector4 : IEqualityComparer<Vector4>
        {
            public bool Equals(Vector4 a, Vector4 b) => (a - b).magnitude < 1e-4f;
            public int GetHashCode(Vector4 v) => 0;
        }

        protected Material Material(string shaderName)
        {
            var shader = Shader.Find(shaderName);
            Assert.That(shader, Is.Not.Null, $"shader {shaderName}");
            return Track(new Material(shader));
        }

        protected Texture2D Texture() => Track(new Texture2D(4, 4));

        protected T Track<T>(T o) where T : Object
        {
            created.Add(o);
            return o;
        }
    }
}
