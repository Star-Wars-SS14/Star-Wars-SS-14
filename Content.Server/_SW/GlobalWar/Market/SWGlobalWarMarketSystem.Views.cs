using Content.Server._SW.GlobalWar.Politics;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._SW.GlobalWar.Market;

public sealed partial class SWGlobalWarMarketSystem
{
    internal bool IsClosingCampaignStep => _campaignStep.IsClosing;

    /// <summary>Resolves the current living assignment, never an ID card or a previously cached faction.</summary>
    internal bool TryGetMarketObserver(EntityUid? viewer, out SWGlobalWarAssignment assignment)
    {
        assignment = default!;
        if (viewer is not { } actor || _campaignGeneration is not { } generation || _campaignStep.IsClosing ||
            !_campaign.TryGetCampaign(out var campaign) || campaign.Comp.Generation != generation ||
            !TryComp<ActorComponent>(actor, out var player) || player.PlayerSession.AttachedEntity != actor ||
            !_assignments.TryGetAssignment(player.PlayerSession.UserId, out var current) ||
            !_assignments.TryGetActor(player.PlayerSession, generation, current.Revision, out assignment))
            return false;
        return assignment.Body == actor;
    }

    internal static bool HasEconomicOverview(SWGlobalWarAssignment assignment)
    {
        return assignment.Role is SWGlobalWarRole.Head or SWGlobalWarRole.WarMinister or
            SWGlobalWarRole.LogisticsMinister or SWGlobalWarRole.IndustryMinister or
            SWGlobalWarRole.LogisticsWorker or SWGlobalWarRole.IndustryWorker;
    }

    /// <summary>Availability is explicit; closed values are omitted before serialization in both Market and Travel.</summary>
    /// <summary>Who holds a planet now, for the sign beside its name on the map. Null without a running campaign.</summary>
    public SWGlobalWarFaction? GetPlanetOwner(ProtoId<SWGlobalWarPlanetPrototype> planet) =>
        Simulation != null && Simulation.Markets.TryGetValue(planet, out var market) ? market.Owner : null;

    public SWGlobalWarMarketAvailability GetMarketAvailability(ProtoId<SWGlobalWarPlanetPrototype> planet,
        EntityUid? viewer = null)
    {
        if (Simulation == null || !TryGetMarket(planet, out var market))
            return SWGlobalWarMarketAvailability.Unavailable;
        if (Simulation.Generation == null)
            return SWGlobalWarMarketAvailability.Ready;
        if (!TryGetMarketObserver(viewer, out var assignment) || !HasEconomicOverview(assignment))
            return SWGlobalWarMarketAvailability.Restricted;
        return market.State.Owner == assignment.Faction
            ? SWGlobalWarMarketAvailability.Ready : SWGlobalWarMarketAvailability.Unknown;
    }
}
