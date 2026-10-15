#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf.localization;

namespace UVWizard.NDMF
{
    /// <summary>
    /// What UV Wizard reports through NDMF's error window. Each message has a title and a description,
    /// which NDMF looks up as key and key:description; {0}, {1}, … are the reported arguments.
    /// </summary>
    internal static class Messages
    {
        public const string AddedTraceAndOptimize = "uvwizard.added-trace-and-optimize";
        public const string Excluded = "uvwizard.excluded";
        public const string Flagged = "uvwizard.flagged";
        public const string NoFit = "uvwizard.no-fit";
        public const string NoToonStandard = "uvwizard.no-toon-standard";
        public const string OverBudget = "uvwizard.over-budget";
        public const string TooManySlots = "uvwizard.too-many-slots";

        static readonly (string Key, string Text)[] English =
        {
            (AddedTraceAndOptimize, "Added AAO Trace and Optimize"),
            (AddedTraceAndOptimize + ":description", "UV Wizard added Avatar Optimizer's Trace and Optimize with its " +
                "default settings, so AAO can merge the avatar's skinned meshes into one. Its defaults don't change how " +
                "the avatar looks. To stop this, turn off Add Trace And Optimize on the UV Wizard component."),
            (Excluded, "Kept out of the atlas: {0}"),
            (Excluded + ":description", "{2} It stays on {1}, on a Toon Standard material of its own."),
            (Flagged, "May look different after the bake: {0}"),
            (Flagged + ":description", "{2} It's on {1}."),
            (NoFit, "Nothing was merged"),
            (NoFit + ":description", "The materials don't fit in the atlas even at the lowest detail, so none is merged. " +
                "Each still gets a Toon Standard material of its own."),
            (NoToonStandard, "VRChat's Toon Standard shader is missing"),
            (OverBudget, "Over the preset's texture memory"),
            (OverBudget + ":description", "After the bake, the avatar's textures take {0} MB, more than the preset's " +
                "{1} MB, even with the smallest atlas. Textures that stay outside the atlas, such as excluded materials' " +
                "own, count too. Choose a lower preset, or reduce those textures."),
            (TooManySlots, "Over the preset's material slots"),
            (TooManySlots + ":description", "After the bake, the avatar will have {0} material slots, but the preset " +
                "allows {1}. Each excluded material keeps a slot of its own. Choose a lower preset, or remove what keeps " +
                "those materials out of the atlas."),
            (NoToonStandard + ":description", "UV Wizard bakes into Toon Standard, which comes with the VRChat SDK. Update the VRChat SDK."),
        };

        public static readonly Localizer Localizer = new Localizer("en-US", () =>
            new List<(string, Func<string, string>)> { ("en-US", Lookup(English)!) });

        static Func<string, string?> Lookup((string Key, string Text)[] strings)
        {
            var texts = strings.ToDictionary(entry => entry.Key, entry => entry.Text);
            return key => texts.TryGetValue(key, out var text) ? text : null;
        }
    }
}
