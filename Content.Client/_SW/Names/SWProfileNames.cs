using System.Text.RegularExpressions;
using Content.Shared.Dataset;
using Content.Shared.Preferences;
using Content.Shared.Random.Helpers;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Client._SW.Names;

/// <summary>Names generated explicitly in the character editor. Other naming paths remain unchanged.</summary>
public static class SWProfileNames
{
    private static readonly Regex Invalid = new("[^А-Яа-яёЁ0-9' -]");

    public static string Generate(string species, Gender gender, int maxLength, bool restrictedNames, IPrototypeManager prototypes)
    {
        var random = IoCManager.Resolve<IRobustRandom>();
        string? name = null;
        // Prefix and ending are localized separately so combinations are not restricted to known characters.
        if (species is "Human" or "Chiss" or "Colicoid" or "Nautolan" or "Twilek" or "Zabrak" or "Zeltron"
            && prototypes.TryIndex<LocalizedDatasetPrototype>($"SWName{species}Start", out var starts)
            && prototypes.TryIndex<LocalizedDatasetPrototype>($"SWName{species}End", out var ends)
            && starts.Values.Count > 0 && ends.Values.Count > 0)
        {
            name = species is "Chiss" or "Colicoid"
                ? random.Pick(starts) + random.Pick(ends)
                : random.Pick(starts) + " " + random.Pick(ends);
        }
        else
        {
            // Use the ordinary generator for unknown species. If its data is missing,
            // keep the editor usable with a valid non-empty fallback.
            try { name = HumanoidCharacterProfile.GetName(species, gender); }
            catch (Exception) { name = "А"; }
        }

        name = name.Trim();
        if (restrictedNames)
            name = Invalid.Replace(name, string.Empty);
        if (name.Length == 0)
            name = "А";
        // A non-positive server limit cannot permit a nonempty name; keep the fallback nonempty.
        if (maxLength > 0 && name.Length > maxLength)
            name = name[..maxLength].TrimEnd();
        return name.Length == 0 ? "А" : name;
    }
}
