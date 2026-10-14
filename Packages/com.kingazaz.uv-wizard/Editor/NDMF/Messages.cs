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
        public const string Excluded = "uvwizard.excluded";
        public const string Flagged = "uvwizard.flagged";
        public const string NoFit = "uvwizard.no-fit";
        public const string NoToonStandard = "uvwizard.no-toon-standard";

        static readonly (string Key, string Text)[] English =
        {
            (Excluded, "Kept out of the atlas: {0}"),
            (Excluded + ":description", "{2} The material stays on {1} as it was."),
            (Flagged, "May look different after the bake: {0}"),
            (Flagged + ":description", "{2} It's merged into the atlas on {1}."),
            (NoFit, "Nothing was merged"),
            (NoFit + ":description", "The materials don't fit in the atlas even at the lowest detail, so the avatar is unchanged."),
            (NoToonStandard, "VRChat's Toon Standard shader is missing"),
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
