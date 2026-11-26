using System.Linq;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Shared._SW.BattlePass;

/// <summary>
/// "Wear" and "take off" from the battle pass window. It writes into the character's ordinary loadouts, so there is one
/// place that says what is worn, and the server checks it as it checks any loadout.
/// </summary>
public static class SWCosmeticWear
{
    public readonly record struct Result(HumanoidCharacterProfile Profile, int Changed, int KeptOther);

    /// <summary>
    /// Puts an item on, in every job that has the group. A job holding the item that most jobs hold follows along: that
    /// is what the last "wear" put there. A job holding something else was set by hand in its own loadout and stays.
    /// Only groups that a job offers are touched, and those exclude jobs whose gear fills the slot.
    /// </summary>
    public static Result Wear(
        HumanoidCharacterProfile profile,
        ProtoId<LoadoutPrototype> item,
        ProtoId<LoadoutGroupPrototype> group,
        ICommonSession? session,
        IEntityManager entMan,
        IPrototypeManager proto)
    {
        var roles = RolesWith(group, proto);
        var common = roles
            .Select(role => Selected(profile, role, group))
            .Where(selected => selected != null)
            .GroupBy(selected => selected)
            .OrderByDescending(same => same.Count())
            .Select(same => same.Key)
            .FirstOrDefault();

        var changed = 0;
        var kept = 0;
        foreach (var role in roles)
        {
            var selected = Selected(profile, role, group);
            if (selected == item.Id)
                continue;

            if (selected != null && selected != common)
            {
                kept++;
                continue;
            }

            var loadout = profile.GetLoadoutOrDefault(role.ID, session, profile.Species, entMan, proto).Clone();
            loadout.SelectedLoadouts[group] = new List<Loadout> { new() { Prototype = item } };
            profile = profile.WithLoadout(loadout);
            changed++;
        }

        return new Result(profile, changed, kept);
    }

    /// <summary>
    /// Takes an item off wherever it is worn, and nothing else.
    /// </summary>
    public static Result TakeOff(
        HumanoidCharacterProfile profile,
        ProtoId<LoadoutPrototype> item,
        ProtoId<LoadoutGroupPrototype> group,
        ICommonSession? session,
        IEntityManager entMan,
        IPrototypeManager proto)
    {
        var changed = 0;
        foreach (var role in RolesWith(group, proto))
        {
            if (Selected(profile, role, group) != item.Id)
                continue;

            var loadout = profile.GetLoadoutOrDefault(role.ID, session, profile.Species, entMan, proto).Clone();
            loadout.SelectedLoadouts[group] = new List<Loadout>();
            profile = profile.WithLoadout(loadout);
            changed++;
        }

        return new Result(profile, changed, 0);
    }

    public static bool IsWorn(HumanoidCharacterProfile profile, ProtoId<LoadoutPrototype> item, ProtoId<LoadoutGroupPrototype> group, IPrototypeManager proto)
    {
        return RolesWith(group, proto).Any(role => Selected(profile, role, group) == item.Id);
    }

    private static List<RoleLoadoutPrototype> RolesWith(ProtoId<LoadoutGroupPrototype> group, IPrototypeManager proto)
    {
        return proto.EnumeratePrototypes<RoleLoadoutPrototype>().Where(role => role.Groups.Contains(group)).ToList();
    }

    private static string? Selected(HumanoidCharacterProfile profile, RoleLoadoutPrototype role, ProtoId<LoadoutGroupPrototype> group)
    {
        return profile.Loadouts.TryGetValue(role.ID, out var loadout)
            && loadout.SelectedLoadouts.TryGetValue(group, out var selected)
            && selected.Count > 0
                ? selected[0].Prototype.Id
                : null;
    }
}
