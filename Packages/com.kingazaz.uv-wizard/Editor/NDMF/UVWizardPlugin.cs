#nullable enable

using nadena.dev.ndmf;
using UVWizard.NDMF;

[assembly: ExportsPlugin(typeof(UVWizardPlugin))]

namespace UVWizard.NDMF
{
    /// <summary>
    /// Runs the bake late in the build: after Modular Avatar and TexTransTool have settled the avatar's
    /// materials, and before Avatar Optimizer merges its meshes, adding the Trace and Optimize that lets it
    /// (ADR 0001).
    /// </summary>
    internal sealed class UVWizardPlugin : Plugin<UVWizardPlugin>
    {
        public override string QualifiedName => "com.kingazaz.uv-wizard";
        public override string DisplayName => "UV Wizard";

        protected override void Configure()
        {
            var sequence = InPhase(BuildPhase.Optimizing)
                .AfterPlugin("nadena.dev.modular-avatar")
                .AfterPlugin("net.rs64.tex-trans-tool")
                .BeforePlugin("com.anatawa12.avatar-optimizer");
            sequence.Run(BakePass.Instance);
#if UVWIZARD_AAO
            sequence.Run(TraceAndOptimizePass.Instance);
#endif
        }
    }
}
