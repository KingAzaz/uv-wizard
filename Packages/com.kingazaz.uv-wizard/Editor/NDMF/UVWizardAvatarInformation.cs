#nullable enable

#if UVWIZARD_AAO
using Anatawa12.AvatarOptimizer.API;

namespace UVWizard.NDMF
{
    /// <summary>
    /// Tells Avatar Optimizer's Trace and Optimize that the UV Wizard component does nothing at runtime, so it
    /// doesn't warn about an unknown component.
    /// </summary>
    [ComponentInformation(typeof(UVWizardAvatar))]
    internal sealed class UVWizardAvatarInformation : ComponentInformation<UVWizardAvatar>
    {
        protected override void CollectDependency(UVWizardAvatar component, ComponentDependencyCollector collector)
        {
        }
    }
}
#endif
