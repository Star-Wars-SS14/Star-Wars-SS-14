using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;

namespace Content.Server._SW.GlobalWar.Politics;

public sealed partial class SWGlobalWarPoliticsSystem
{
    /// <summary>Political NPC gate; callers separately validate hostility and the NPC's physical location.</summary>
    public bool CanConductNpcHostility(EntityUid planet, Guid generation)
    {
        return TryGetPolitics(out var politics) && politics.Comp.Generation == generation &&
               _campaign.TryGetCampaign(out var campaign) && campaign.Comp.Phase == SWGlobalWarCampaignPhase.Running &&
               TryComp<SWGlobalWarPlanetComponent>(planet, out var world) &&
               world.GlobalWarGamerule == politics.Owner && CombatOpen(politics.Comp, world);
    }

    /// <summary>
    /// Revalidates the political conditions of an existing physical operation independently of its first operator.
    /// Starting or holding an operation still requires a real actor through CanCapture.
    /// </summary>
    public bool CanMaintainCapture(SWGlobalWarFaction faction, EntityUid planet,
        SWGlobalWarPlanetLocation location, Guid generation)
    {
        if (faction is not (SWGlobalWarFaction.Republic or SWGlobalWarFaction.Empire) ||
            !Enum.IsDefined(location) || !TryGetPolitics(out var politics) ||
            politics.Comp.Generation != generation || !_campaign.TryGetCampaign(out var campaign) ||
            campaign.Comp.Phase != SWGlobalWarCampaignPhase.Running ||
            !TryComp<SWGlobalWarPlanetComponent>(planet, out var target) ||
            target.GlobalWarGamerule != politics.Owner || !CombatOpen(politics.Comp, target))
            return false;
        if (location == SWGlobalWarPlanetLocation.Orbit)
            return target.OrbitOwner != faction;
        return target.SurfaceOwner != faction && target.OrbitOwner == faction &&
               (!politics.Comp.Neutrals.TryGetValue(target.Prototype, out var neutral) ||
                neutral.Phase == SWGlobalWarNeutralPhase.Integrated);
    }
}
