#nullable enable

using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UVWizard.Core.Pipeline;
using UVWizard.Core.Reporting;

namespace UVWizard.Core.Tests.Pipeline
{
    // Particle, trail and line materials converted to VRChat's mobile particle shaders (#148). Unity's legacy
    // Additive and Multiply particle shaders stand in for VRChat's, which CI's project doesn't have. Sources use
    // Particles/Standard Unlit, whose blend is in its _SrcBlend and _DstBlend floats.
    public partial class AvatarBakerTests
    {
        static readonly Shader AdditiveStandIn = Shader.Find("Legacy Shaders/Particles/Additive");
        static readonly Shader MultiplyStandIn = Shader.Find("Legacy Shaders/Particles/Multiply");

        [TestCase(BlendMode.SrcAlpha, BlendMode.One)]
        [TestCase(BlendMode.One, BlendMode.One)]
        public void AnAdditiveParticleMaterialGetsTheAdditiveShaderAndKeepsItsTextureAndTiling(BlendMode src, BlendMode dst)
        {
            var source = Particle(src, dst, Gradient(16, 1), Color.white, new Vector4(2, 1, 0.1f, 0.5f));
            var particles = ParticleSystemRenderer(source);

            var result = Bake(ConvertingParticles(), particles);

            var converted = particles.sharedMaterial;
            Assert.That(converted, Is.Not.SameAs(source));
            Assert.That(converted.shader, Is.SameAs(AdditiveStandIn));
            Assert.That(converted.mainTexture, Is.SameAs(source.mainTexture), "a white tint needs no texture of its own");
            Assert.That(converted.mainTextureScale, Is.EqualTo(new Vector2(2, 1)));
            Assert.That(converted.mainTextureOffset, Is.EqualTo(new Vector2(0.1f, 0.5f)));
            Assert.That(result.ParticleMaterials, Is.EqualTo(new[] { (source, converted) }));
            Assert.That(result.Unconverted, Is.Empty);
            Assert.That(result.Flagged.Select(note => note.Reason), Is.EqualTo(new[] { UnknownParticleShader }));
        }

        [TestCase(BlendMode.Zero, BlendMode.SrcColor)]
        [TestCase(BlendMode.DstColor, BlendMode.Zero)]
        public void AMultiplyTrailMaterialGetsTheMultiplyShader(BlendMode src, BlendMode dst)
        {
            var source = Particle(src, dst, Gradient(16, 1), Color.white);
            var trail = Keep(new GameObject("trail")).AddComponent<TrailRenderer>();
            trail.sharedMaterials = new[] { source };

            var result = Bake(ConvertingParticles(), trail);

            Assert.That(trail.sharedMaterial.shader, Is.SameAs(MultiplyStandIn));
            Assert.That(result.Unconverted, Is.Empty);
            Assert.That(result.Flagged.Select(note => note.Reason), Is.EqualTo(new[] { UnknownParticleShader }));
        }

        [Test]
        public void AnAlphaBlendedLineMaterialDrawsAdditivelyNowAndSaysSo()
        {
            var source = Particle(BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha, Gradient(16, 1), Color.white);
            var line = LineRenderer(source);

            var result = Bake(ConvertingParticles(), line);

            Assert.That(line.sharedMaterial.shader, Is.SameAs(AdditiveStandIn));
            Assert.That(result.Unconverted, Is.Empty);
            Assert.That(result.Flagged.Select(note => (note.Renderer, note.Material, note.Reason)),
                Is.EquivalentTo(new[] { ((Renderer)line, source, new Reason(ReasonText.DrawsAdditively)),
                    ((Renderer)line, source, UnknownParticleShader) }));
        }

        [Test]
        public void ASubtractiveParticleMaterialDrawsAdditivelyNowAndSaysSo()
        {
            // Particles/Standard's Subtractive mode blends SrcAlpha One with a reverse-subtract operation (probed
            // through its shader GUI's ValidateMaterial): the additive pair, but it darkens.
            var source = Particle(BlendMode.SrcAlpha, BlendMode.One, Gradient(16, 1), Color.white);
            source.SetFloat("_BlendOp", (float)BlendOp.ReverseSubtract);
            var particles = ParticleSystemRenderer(source);

            var result = Bake(ConvertingParticles(), particles);

            Assert.That(particles.sharedMaterial.shader, Is.SameAs(AdditiveStandIn));
            Assert.That(result.Flagged.Select(note => note.Reason), Has.Member(new Reason(ReasonText.DrawsAdditively)));
        }

        [Test]
        public void AParticleMaterialOnAShaderUvWizardDoesntKnowSaysOnlyItsTextureAndColourAreRead()
        {
            // Unity's legacy particle shaders tint by _TintColor, which no reader reads.
            var source = Keep(new Material(Shader.Find("Legacy Shaders/Particles/Alpha Blended")) { mainTexture = Gradient(16, 1) });
            var line = LineRenderer(source);

            var result = Bake(ConvertingParticles(), line);

            Assert.That(result.Flagged.Select(note => note.Reason), Has.Member(
                new Reason(ReasonText.UnknownShader, "Legacy Shaders/Particles/Alpha Blended")));
        }

        [Test]
        public void ParticleRenderersSharingAMaterialShareItsParticleMaterialAndOneNote()
        {
            var source = Particle(BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha, Gradient(16, 1), Color.white);
            var first = LineRenderer(source);
            var second = LineRenderer(source);

            var result = Bake(ConvertingParticles(), first, second);

            Assert.That(second.sharedMaterial, Is.SameAs(first.sharedMaterial));
            Assert.That(result.ParticleMaterials, Has.Count.EqualTo(1));
            Assert.That(result.Flagged.Count(note => note.Reason.Equals(new Reason(ReasonText.DrawsAdditively))), Is.EqualTo(1));
        }

        [Test]
        public void AParticleMaterialWithNoBlendPropertiesDrawsAdditivelyNowAndSaysSo()
        {
            // Unlit/Transparent hard-codes its blend in its pass, so the material has no _SrcBlend or _DstBlend.
            var source = Keep(new Material(Shader.Find("Unlit/Transparent")) { mainTexture = Gradient(16, 1) });
            var line = LineRenderer(source);

            var result = Bake(ConvertingParticles(), line);

            Assert.That(line.sharedMaterial.shader, Is.SameAs(AdditiveStandIn));
            Assert.That(line.sharedMaterial.mainTexture, Is.SameAs(source.mainTexture));
            Assert.That(result.Flagged.Select(note => note.Reason), Has.Member(new Reason(ReasonText.DrawsAdditively)));
        }

        [Test]
        public void AParticleMaterialsTintIsBakedIntoItsTextureWithItsAlpha()
        {
            // Point-filtered, so each texel's centre samples that texel alone.
            var texture = Keep(new Texture2D(4, 4, TextureFormat.RGBA32, mipChain: false, linear: false)
                { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat });
            var texels = new Color32[16];
            for (var i = 0; i < texels.Length; i++)
                texels[i] = new Color32((byte)(40 + 13 * i), (byte)(200 - 9 * i), 120, (byte)(255 - 15 * i));
            texture.SetPixels32(texels);
            texture.Apply();
            var tint = new Color(1, 0.5f, 0.25f, 0.5f);
            var source = Particle(BlendMode.SrcAlpha, BlendMode.One, texture, tint, new Vector4(3, 2, 0.25f, 0));
            var particles = ParticleSystemRenderer(source);

            Bake(ConvertingParticles(), particles);

            var converted = particles.sharedMaterial;
            var baked = (Texture2D)converted.mainTexture;
            Assert.That(baked, Is.Not.SameAs(texture));
            Assert.That((baked.width, baked.height), Is.EqualTo((4, 4)));
            Assert.That(baked.wrapMode, Is.EqualTo(TextureWrapMode.Repeat));
            Assert.That(converted.mainTextureScale, Is.EqualTo(new Vector2(3, 2)));
            Assert.That(converted.mainTextureOffset, Is.EqualTo(new Vector2(0.25f, 0)));
            // The bake multiplies in linear space, as the shaders draw in this Linear-space project.
            for (var y = 0; y < 4; y++)
            for (var x = 0; x < 4; x++)
            {
                var from = texture.GetPixel(x, y);
                var expected = (from.linear * tint.linear).gamma;
                expected.a = from.a * tint.a;
                var actual = baked.GetPixel(x, y);
                for (var channel = 0; channel < 4; channel++)
                    Assert.That(actual[channel], Is.EqualTo(expected[channel]).Within(0.02f), $"texel ({x}, {y}), channel {channel}");
            }
        }

        [Test]
        public void ParticleRenderersThatDontUploadAndDesktopBakesKeepTheirMaterials()
        {
            var source = Particle(BlendMode.SrcAlpha, BlendMode.One, Gradient(16, 1), Color.white);
            var editorOnly = ParticleSystemRenderer(source);
            editorOnly.tag = "EditorOnly";
            var desktop = LineRenderer(Particle(BlendMode.SrcAlpha, BlendMode.One, Gradient(16, 2), Color.white));
            var kept = desktop.sharedMaterial;

            var mobile = Bake(ConvertingParticles(), editorOnly);
            var pc = Bake(new BakeOptions(Shader.Find("Standard"), padding: 0,
                particleShaders: new ParticleShaders(AdditiveStandIn, MultiplyStandIn)), desktop);

            Assert.That(editorOnly.sharedMaterial, Is.SameAs(source));
            Assert.That(desktop.sharedMaterial, Is.SameAs(kept));
            Assert.That(mobile.ParticleMaterials.Concat(pc.ParticleMaterials), Is.Empty);
            Assert.That(mobile.Unconverted.Concat(pc.Unconverted), Is.Empty);
        }

        [Test]
        public void AMaterialOnAMeshAndOnParticlesGetsAConversionForEach()
        {
            var source = Particle(BlendMode.SrcAlpha, BlendMode.One, Gradient(16, 1), Color.white);
            var mesh = MeshRenderer(Mesh(new[] { Quad(0, 0, 0, 5, 1) }), source);
            var particles = ParticleSystemRenderer(source);

            var result = Bake(ConvertingParticles(), mesh, particles);

            Assert.That(mesh.sharedMaterial.shader, Is.SameAs(Shader.Find("Standard")));
            Assert.That(particles.sharedMaterial.shader, Is.SameAs(AdditiveStandIn));
            Assert.That(result.ConvertedMaterials, Is.EqualTo(new[] { (source, mesh.sharedMaterial) }));
            Assert.That(result.ParticleMaterials, Is.EqualTo(new[] { (source, particles.sharedMaterial) }));
        }

        [Test]
        public void AMaterialAnAnimationSwapsOntoALineIsLeftAndReported()
        {
            // Only a mesh renderer's swaps are pointed at their conversions, so a particle material made for the swap
            // would go unused.
            var own = Particle(BlendMode.SrcAlpha, BlendMode.One, Gradient(16, 1), Color.white);
            var swapped = Particle(BlendMode.SrcAlpha, BlendMode.One, Gradient(16, 2), Color.white);
            var line = LineRenderer(own);

            var result = Bake(ConvertingParticles(), new Renderer[] { line },
                new AnimatedSlot(line, 0, new Reason("A"), new[] { swapped }));

            Assert.That(line.sharedMaterial.shader, Is.SameAs(AdditiveStandIn));
            Assert.That(result.ParticleMaterials.Select(p => p.Source), Is.EqualTo(new[] { own }));
            Assert.That(result.Unconverted.Select(note => (note.Material, note.Reason)),
                Is.EqualTo(new[] { (swapped, new Reason(ReasonText.SwappedOntoParticles)) }));
        }

        [Test]
        public void WithoutParticleShadersAParticleMaterialIsLeftAndReported()
        {
            var source = Particle(BlendMode.SrcAlpha, BlendMode.One, Gradient(16, 1), Color.white);
            var particles = ParticleSystemRenderer(source);

            var result = Bake(Converting(), particles);

            Assert.That(particles.sharedMaterial, Is.SameAs(source));
            Assert.That(result.Unconverted.Select(note => note.Reason), Is.EqualTo(new[] { new Reason(ReasonText.MeshRenderersOnly) }));
        }

        static readonly Reason UnknownParticleShader = new Reason(ReasonText.UnknownShader, "Particles/Standard Unlit");

        // A Particles/Standard Unlit material with the given blend, texture, tint and tiling.
        Material Particle(BlendMode src, BlendMode dst, Texture2D texture, Color tint, Vector4? scaleOffset = null)
        {
            var material = Keep(new Material(Shader.Find("Particles/Standard Unlit")) { mainTexture = texture, color = tint });
            material.SetFloat("_SrcBlend", (float)src);
            material.SetFloat("_DstBlend", (float)dst);
            var st = scaleOffset ?? new Vector4(1, 1, 0, 0);
            material.mainTextureScale = new Vector2(st.x, st.y);
            material.mainTextureOffset = new Vector2(st.z, st.w);
            return material;
        }

        ParticleSystemRenderer ParticleSystemRenderer(Material material)
        {
            var go = Keep(new GameObject("particles"));
            go.AddComponent<ParticleSystem>();
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterials = new[] { material };
            return renderer;
        }

        LineRenderer LineRenderer(Material material)
        {
            var line = Keep(new GameObject("line")).AddComponent<LineRenderer>();
            line.sharedMaterials = new[] { material };
            return line;
        }

        static BakeOptions ConvertingParticles() =>
            new BakeOptions(Shader.Find("Standard"), padding: 0, mobileShaders: System.Array.Empty<string>(),
                particleShaders: new ParticleShaders(AdditiveStandIn, MultiplyStandIn));
    }
}
