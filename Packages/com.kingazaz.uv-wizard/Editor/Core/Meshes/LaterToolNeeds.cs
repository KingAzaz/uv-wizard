#nullable enable

using UVWizard.Core.Reporting;

namespace UVWizard.Core.Meshes
{
    /// <summary>
    /// How a renderer's rewritten mesh must look for a tool that runs after the bake, such as Avatar Optimizer.
    /// The default needs nothing kept.
    /// </summary>
    internal readonly struct LaterToolNeeds
    {
        /// <summary>
        /// The tool reads the source UV0, but the bake can't keep it where the tool would find it, such as when
        /// the mesh has no free channel to keep it in. The renderer stays as it is.
        /// </summary>
        public static readonly LaterToolNeeds CantKeepSourceUv0 = KeepsOut(new Reason(ReasonText.LaterToolNeedsUvs));

        /// <summary>The tool needs the renderer as it is, for the reason given, so it stays out of the bake.</summary>
        public static LaterToolNeeds KeepsOut(Reason why) => new LaterToolNeeds(null, false, why);

        /// <param name="keepSourceUv0In">
        /// A UV channel, 1 to 7, that the mesh doesn't use, to hold every copy's UV0 as the source has it, for a
        /// tool that reads the source UVs. Null keeps no copy.
        /// </param>
        /// <param name="keepSubmeshes">
        /// Keeps every merged submesh in its own place, for a tool that reads the mesh per material slot.
        /// </param>
        public LaterToolNeeds(int? keepSourceUv0In, bool keepSubmeshes) : this(keepSourceUv0In, keepSubmeshes, null)
        {
        }

        LaterToolNeeds(int? keepSourceUv0In, bool keepSubmeshes, Reason? keptOutBecause)
        {
            KeepSourceUv0In = keepSourceUv0In;
            KeepSubmeshes = keepSubmeshes;
            KeptOutBecause = keptOutBecause;
        }

        /// <summary>The UV channel to keep the source UV0 in, or null to keep no copy.</summary>
        public int? KeepSourceUv0In { get; }

        /// <summary>Whether every merged submesh keeps its own place.</summary>
        public bool KeepSubmeshes { get; }

        /// <summary>
        /// Why the renderer stays out of the bake, as <see cref="KeepsOut"/> gave it, or null when it's baked.
        /// </summary>
        public Reason? KeptOutBecause { get; }
    }
}
