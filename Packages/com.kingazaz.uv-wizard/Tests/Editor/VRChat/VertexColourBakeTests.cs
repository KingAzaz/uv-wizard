#nullable enable

using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UVWizard.Core.Pipeline;
using UVWizard.Core.Reporting;

namespace UVWizard.Core.Tests.VRChat
{
    // Standard Lite multiplies in vertex colour. It ships with the VRChat SDK, which CI's project doesn't have.
    public class VertexColourBakeTests : ObjectFixture
    {
        [Test]
        public void ASourceOnColouredVerticesIsFlaggedWhenTheOptionsVertexColourToggleTurnsOn()
        {
            // Standard's smoothness stands in for the toggle, which Standard Lite turns on.
            var lite = Keep(new Material(Shader.Find("VRChat/Mobile/Standard Lite")));
            var standard = Keep(new Material(Shader.Find("Standard")));
            var renderers = new[] { MeshRenderer(Quad(0.1f, Color.white), lite), MeshRenderer(Quad(0.6f, Color.red), standard) };
            var options = new BakeOptions(Shader.Find("Standard"), names: new MergedShaderNames(vertexColour: "_Glossiness"));

            var result = AvatarBaker.Bake(renderers, options);
            Keep(result.MergedMaterial!);
            Keep(result.Atlas!);
            foreach (var renderer in renderers) Keep(renderer.GetComponent<MeshFilter>().sharedMesh);

            Assume.That(result.MergedMaterial!.GetFloat("_Glossiness"), Is.EqualTo(1));
            Assert.That(result.Flagged.Where(note => note.Reason.Keys.Contains(ReasonText.VertexColours)).Select(note => note.Material),
                Is.EqualTo(new[] { standard }));
        }

        // A quad from UV (min, min) to (min + 0.3, min + 0.3), every vertex the given colour.
        Mesh Quad(float min, Color colour)
        {
            var mesh = Keep(new Mesh());
            var uvs = new[]
            {
                new Vector2(min, min), new Vector2(min + 0.3f, min), new Vector2(min, min + 0.3f), new Vector2(min + 0.3f, min + 0.3f),
            };
            mesh.SetVertices(uvs.Select(uv => (Vector3)uv).ToArray());
            mesh.SetUVs(0, uvs);
            mesh.SetColors(Enumerable.Repeat(colour, 4).ToArray());
            mesh.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0);
            return mesh;
        }

        MeshRenderer MeshRenderer(Mesh mesh, Material material)
        {
            var go = Keep(new GameObject("renderer"));
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            return renderer;
        }
    }
}
