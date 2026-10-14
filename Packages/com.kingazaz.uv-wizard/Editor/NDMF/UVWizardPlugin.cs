#nullable enable

using nadena.dev.ndmf;
using UVWizard.NDMF;

[assembly: ExportsPlugin(typeof(UVWizardPlugin))]

namespace UVWizard.NDMF
{
    /// <summary>
    /// Runs the bake late in the build: after Modular Avatar and TexTransTool have settled the avatar's
    /// materials, and before Avatar Optimizer merges its meshes (ADR 0001).
    /// </summary>
    internal sealed class UVWizardPlugin : Plugin<UVWizardPlugin>
    {
        public override string QualifiedName => "com.kingazaz.uv-wizard";
        public override string DisplayName => "UV Wizard";

        protected override void Configure()
        {
            InPhase(BuildPhase.Optimizing)
                .AfterPlugin("nadena.dev.modular-avatar")
                .AfterPlugin("net.rs64.tex-trans-tool")
                .BeforePlugin("com.anatawa12.avatar-optimizer")
                .Run(BakePass.Instance);
        }
    }
}
