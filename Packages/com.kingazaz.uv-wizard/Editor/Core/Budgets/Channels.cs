#nullable enable

using System;

namespace UVWizard.Core.Budgets
{
    /// <summary>The channels the atlas may carry. Each can be turned on or off.</summary>
    [Flags]
    internal enum Channels
    {
        None = 0,
        Albedo = 1,
        Emission = 2,
        Normal = 4,
        All = Albedo | Emission | Normal,
    }
}
