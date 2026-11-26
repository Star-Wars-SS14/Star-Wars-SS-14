using System.Linq;
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
    private static readonly Regex NameCase = new(@"^(?<word>\w)|\b(?<word>\w)(?=\w*$)");
    private static readonly Regex Invalid = new("[^А-Яа-яёЁ0-9' -]");

    public static string Generate(string species, Gender gender, int maxLength, bool restrictedNames, IPrototypeManager prototypes, bool icNameCase = false)
    {
        var random = IoCManager.Resolve<IRobustRandom>();
        string? name = null;
        if (species == "Chiss"
            && prototypes.TryIndex<LocalizedDatasetPrototype>("SWNameChissStart", out var families)
            && prototypes.TryIndex<LocalizedDatasetPrototype>("SWNameChissCoreStart", out var familyCores)
            && prototypes.TryIndex<LocalizedDatasetPrototype>("SWNameChissMiddle", out var given)
            && prototypes.TryIndex<LocalizedDatasetPrototype>("SWNameChissEnd", out var endings)
            && prototypes.TryIndex<LocalizedDatasetPrototype>("SWNameChissCoreEnd", out var endingCores)
            && families.Values.Count > 0 && families.Values.Count == familyCores.Values.Count
            && endings.Values.Count > 0 && endings.Values.Count == endingCores.Values.Count && given.Values.Count > 0)
        {
            var family = random.Next(families.Values.Count);
            var ending = random.Next(endings.Values.Count);
            var personal = random.Pick(given);
            // Core fragments are explicitly paired with their full components, not sliced from them.
            name = random.Prob(0.5f)
                ? Loc.GetString(families.Values[family]) + "'" + personal + "'" + Loc.GetString(endings.Values[ending])
                : Loc.GetString(familyCores.Values[family]) + personal + Loc.GetString(endingCores.Values[ending]);
            if (name.Length > 0)
                name = string.Concat(char.ToUpperInvariant(name[0]).ToString(), name.Substring(1));
        }
        else if (species == "Colicoid"
                 && prototypes.TryIndex<LocalizedDatasetPrototype>("SWNameColicoidStart", out var colicoid)
                 && colicoid.Values.Count > 0)
        {
            name = random.Pick(colicoid);
        }
        else if (species is "Human" or "Nautolan" or "Twilek" or "Zabrak" or "Zeltron"
                 && prototypes.TryIndex<LocalizedDatasetPrototype>($"SWName{species}Start", out var starts)
                 && prototypes.TryIndex<LocalizedDatasetPrototype>($"SWName{species}End", out var ends)
                 && starts.Values.Count > 0 && ends.Values.Count > 0)
        {
            name = random.Pick(starts);
            if (random.Prob(0.75f))
                name += " " + random.Pick(ends);
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
        if (name.Length == 0 || !name.Any(char.IsLetterOrDigit))
            name = "А";
        // A non-positive server limit cannot permit a nonempty name; keep the fallback nonempty.
        if (maxLength > 0 && name.Length > maxLength)
            name = name[..maxLength].TrimEnd();
        if (name.Length == 0)
            name = "А";
        if (species == "Chiss" && icNameCase)
            name = NameCase.Replace(name, match => match.Groups["word"].Value.ToUpper());
        return name;
    }
}
