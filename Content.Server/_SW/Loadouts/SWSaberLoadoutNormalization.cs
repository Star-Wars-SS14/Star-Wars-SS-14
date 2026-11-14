using Content.Shared.Preferences.Loadouts;
using Robust.Shared.Prototypes;

namespace Content.Server._SW.Loadouts;

/// <summary>Fills the required saber choices for force-user spawns outside the normal preference validation path.</summary>
public static class SWSaberLoadoutNormalization
{
    public static RoleLoadout Normalize(string job, RoleLoadout loadout, RoleLoadoutPrototype role, IPrototypeManager prototypes)
    {
        var crystalGroup = job switch
        {
            "SWJedi" or "SWGlobalWarRepublicForceUser" => "SWJediCrystalChoice",
            "SWSith" or "SWGlobalWarEmpireForceUser" => "SWSithCrystalChoice",
            _ => null,
        };
        if (crystalGroup == null)
            return loadout;

        var copy = loadout.Clone();

        // Membership and limits are structural checks only; effects still require the normal session validation.
        foreach (var id in new List<ProtoId<LoadoutGroupPrototype>>(copy.SelectedLoadouts.Keys))
        {
            if (!role.Groups.Contains(id))
                copy.SelectedLoadouts.Remove(id);
        }

        foreach (var id in role.Groups)
            SanitizeGroup(copy, prototypes, id);

        NormalizeGroup(copy, role, prototypes, "SWSaberHiltChoice");
        NormalizeGroup(copy, role, prototypes, "SWSaberEmitterChoice");
        NormalizeGroup(copy, role, prototypes, crystalGroup);
        return copy;
    }

    private static void SanitizeGroup(RoleLoadout loadout, IPrototypeManager prototypes,
        ProtoId<LoadoutGroupPrototype> groupId)
    {
        if (!prototypes.TryIndex(groupId, out var group))
        {
            loadout.SelectedLoadouts.Remove(groupId);
            return;
        }

        var valid = new List<Loadout>();
        if (loadout.SelectedLoadouts.TryGetValue(groupId, out var selected))
        {
            foreach (var item in selected)
            {
                if (valid.Count >= group.MaxLimit)
                    break;
                if (!group.Loadouts.Contains(item.Prototype) ||
                    !prototypes.TryIndex(item.Prototype, out LoadoutPrototype? _) || valid.Contains(item))
                    continue;
                valid.Add(item);
            }
        }

        // A missing mandatory group gets only a default that needs no effect authorization.
        foreach (var id in group.Loadouts)
        {
            if (valid.Count >= group.MinLimit || valid.Count >= group.MaxLimit)
                break;
            if (!prototypes.TryIndex(id, out var candidate) || candidate.Effects.Count != 0)
                continue;
            var item = new Loadout { Prototype = id };
            if (!valid.Contains(item))
                valid.Add(item);
        }

        loadout.SelectedLoadouts[groupId] = valid;
    }

    private static void NormalizeGroup(RoleLoadout loadout, RoleLoadoutPrototype role,
        IPrototypeManager prototypes, ProtoId<LoadoutGroupPrototype> groupId)
    {
        if (!role.Groups.Contains(groupId) || !prototypes.TryIndex(groupId, out var group))
            return;

        if (loadout.SelectedLoadouts.TryGetValue(groupId, out var selected) && selected.Count > 0 &&
            group.Loadouts.Contains(selected[0].Prototype) &&
            prototypes.TryIndex(selected[0].Prototype, out var selectedProto) && selectedProto.Effects.Count == 0)
        {
            loadout.SelectedLoadouts[groupId] = [selected[0]];
            return;
        }

        // These groups currently have no effects. Never bypass future effect checks without a session.
        loadout.SelectedLoadouts[groupId] = [];
        foreach (var id in group.Loadouts)
        {
            if (!prototypes.TryIndex(id, out var candidate) || candidate.Effects.Count != 0)
                continue;
            loadout.SelectedLoadouts[groupId].Add(new Loadout { Prototype = id });
            break;
        }
    }
}
