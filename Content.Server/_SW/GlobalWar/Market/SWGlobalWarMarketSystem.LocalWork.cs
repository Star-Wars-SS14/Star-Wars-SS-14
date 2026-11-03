using System.Linq;
using Content.Shared._SW.GlobalWar;
using Robust.Shared.Player;

namespace Content.Server._SW.GlobalWar.Market;

public sealed partial class SWGlobalWarMarketSystem
{
    /// <summary>
    /// Reads currently available local delivery work for tutorial navigation. Does not reserve, publish,
    /// assign, manufacture, pay, or relax the normal work command's authority and proximity checks.
    /// </summary>
    public IReadOnlyList<SWGlobalWarSupplyDeliveryView> GetLocalWorkSuggestions(ICommonSession session)
    {
        if (Simulation is not { Generation: { } generation } || !CanMutateCampaign(generation) ||
            !_assignments.TryGetAssignment(session.UserId, out var current) ||
            !_assignments.TryGetActor(session, generation, current.Revision, out var actor))
            return Array.Empty<SWGlobalWarSupplyDeliveryView>();
        if (!_hubPlanets.TryGetPhysicalLocation(actor.Body, out var planet, out var location) ||
            location != SWGlobalWarPlanetLocation.Surface)
            return Array.Empty<SWGlobalWarSupplyDeliveryView>();
        var world = Comp<SWGlobalWarPlanetComponent>(planet).Prototype.Id;
        return GetSupplyWorkBoard(actor)?.Deliveries.Where(d => d.Source == world && d.Recipient == world).ToArray() ?? [];
    }
}
