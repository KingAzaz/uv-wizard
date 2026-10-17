#nullable enable

using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UVWizard.Core.Tests
{
    /// <summary>
    /// Destroys the objects a test keeps after it.
    /// </summary>
    public abstract class ObjectFixture
    {
        readonly List<Object> created = new List<Object>();

        [TearDown]
        public void DestroyCreated()
        {
            // A test may destroy what it kept, and NDMF saves what a build references into its own temporary assets,
            // which it cleans up itself.
            foreach (var o in created)
                if (o != null && !EditorUtility.IsPersistent(o)) Object.DestroyImmediate(o);
            created.Clear();
        }

        protected T Keep<T>(T o) where T : Object
        {
            created.Add(o);
            return o;
        }
    }
}
