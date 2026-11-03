using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Content.Server.Shuttles.Components;
using Robust.Shared.Map.Components;

namespace Content.Server._SW.GlobalWar.Politics;

/// <summary>Server-only property records consumed by withdrawal and later committed ship construction/capture.</summary>
public sealed class SWGlobalWarShipOwnershipSystem : EntitySystem
{
    [Dependency] private readonly SWGlobalWarCampaignSystem _campaign = default!;
    [Dependency] private readonly SWGlobalWarGameruleSystem _rules = default!;
    [Dependency] private readonly SWGlobalWarPlanetSystem _planets = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<SWGlobalWarShipOwnershipComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<SWGlobalWarCampaignStartedEvent>(OnStarted);
        SubscribeLocalEvent<SWGlobalWarLocationReadyEvent>(OnLocationReady);
    }

    private void OnMapInit(Entity<SWGlobalWarShipOwnershipComponent> ent, ref MapInitEvent args)
    {
        if (_campaign.TryGetCampaign(out var campaign))
            RegisterShip(ent.Owner, campaign.Comp.Generation, ent.Comp.Owner);
    }

    private void OnStarted(ref SWGlobalWarCampaignStartedEvent args)
    {
        var query = AllEntityQuery<SWGlobalWarShipOwnershipComponent>();
        while (query.MoveNext(out var ship, out var ownership))
            RegisterShip(ship, args.Generation, ownership.Owner);
    }

    private void OnLocationReady(ref SWGlobalWarLocationReadyEvent args)
    {
        if (!_campaign.TryGetCampaign(out var campaign) || campaign.Comp.Generation != args.Generation)
            return;
        var query = AllEntityQuery<SWGlobalWarShipOwnershipComponent>();
        while (query.MoveNext(out var ship, out var ownership))
            if (Transform(ship).MapUid == args.Map)
                RegisterShip(ship, args.Generation, ownership.Owner);
    }

    /// <summary>
    /// Registers an existing ship once after a server construction or mapped-ship spawn commits.
    /// This is not a player command and must never be called with a faction taken from navigation input.
    /// </summary>
    public bool RegisterShip(EntityUid ship, Guid generation, SWGlobalWarFaction owner)
    {
        if (owner is not (SWGlobalWarFaction.Republic or SWGlobalWarFaction.Empire) ||
            !_campaign.TryGetCampaign(out var campaign) || campaign.Comp.Generation != generation ||
            campaign.Comp.Phase != SWGlobalWarCampaignPhase.Running ||
            !_rules.TryGetGlobalWarGamerule(out var rule) || !HasComp<MapGridComponent>(ship) ||
            !HasComp<ShuttleComponent>(ship) || TerminatingOrDeleted(ship))
            return false;
        foreach (var planet in rule.Comp.PlanetEntities.Values)
            if (_planets.IsPlanetStationGrid(planet, ship))
                return false;
        var ownership = EnsureComp<SWGlobalWarShipOwnershipComponent>(ship);
        if (ownership.Generation != Guid.Empty)
            return ownership.Generation == generation && ownership.Owner == owner;
        ownership.Generation = generation;
        ownership.Owner = owner;
        return true;
    }

    /// <summary>Reads current-generation ownership, excluding original station grids even if incorrectly tagged.</summary>
    public bool TryGetOwner(EntityUid ship, Guid generation, out SWGlobalWarFaction owner, out long revision)
    {
        owner = default;
        revision = 0;
        if (!_rules.TryGetGlobalWarGamerule(out var rule) || rule.Comp.Generation != generation ||
            !TryComp<SWGlobalWarShipOwnershipComponent>(ship, out var ownership) || ownership.Generation != generation ||
            TerminatingOrDeleted(ship) || !HasComp<MapGridComponent>(ship) || !HasComp<ShuttleComponent>(ship))
            return false;
        foreach (var planet in rule.Comp.PlanetEntities.Values)
            if (_planets.IsPlanetStationGrid(planet, ship))
                return false;
        owner = ownership.Owner;
        revision = ownership.Revision;
        return owner is SWGlobalWarFaction.Republic or SWGlobalWarFaction.Empire;
    }

    /// <summary>F calls this only after its physical ship capture commits; repeat receipts cannot transfer twice.</summary>
    public bool CommitOwnership(EntityUid ship, Guid generation, long expectedRevision, Guid operation,
        SWGlobalWarFaction owner)
    {
        if (operation == Guid.Empty || owner is not (SWGlobalWarFaction.Republic or SWGlobalWarFaction.Empire) ||
            !TryGetOwner(ship, generation, out _, out var revision))
            return false;
        var ownership = Comp<SWGlobalWarShipOwnershipComponent>(ship);
        if (ownership.Operations.TryGetValue(operation, out var receipt))
            return receipt.Owner == owner;
        if (revision != expectedRevision || !_campaign.TryGetCampaign(out var campaign) ||
            campaign.Comp.Phase != SWGlobalWarCampaignPhase.Running)
            return false;
        var previous = ownership.Owner;
        ownership.Owner = owner;
        ownership.Revision++;
        ownership.Operations.Add(operation, (owner, ownership.Revision));
        var ev = new SWGlobalWarShipOwnershipChangedEvent(ship, generation, previous, owner, ownership.Revision, operation);
        RaiseLocalEvent(ref ev);
        return true;
    }
}

[ByRefEvent]
public readonly record struct SWGlobalWarShipOwnershipChangedEvent(EntityUid Ship, Guid Generation,
    SWGlobalWarFaction PreviousOwner, SWGlobalWarFaction Owner, long Revision, Guid Operation);
