using System.Linq;
using Content.Server._SW.GlobalWar.Market;
using Content.Server._SW.GlobalWar.Politics;
using Content.Server._SW.GlobalWar.Production;
using Content.Server._SW.Targeting;
using Content.Server.Popups;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared._SW.Droids;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Combat;
using Content.Shared._SW.GlobalWar.Politics;
using Content.Shared.ActionBlocker;
using Content.Shared.Interaction;
using Content.Shared.Power;
using Robust.Server.GameObjects;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._SW.GlobalWar.Combat;

[RegisterComponent]
public sealed partial class SWGlobalWarReconDatapadComponent : Component;

/// <summary>Intelligence reports require physical access and retain only an explicit bounded observation.</summary>
public sealed class SWGlobalWarReconSystem : EntitySystem
{
    [Dependency] private readonly SWGlobalWarCampaignSystem _campaign = default!;
    [Dependency] private readonly SWGlobalWarCampaignStepSystem _steps = default!;
    [Dependency] private readonly SWGlobalWarAssignmentsSystem _assignments = default!;
    [Dependency] private readonly SWGlobalWarPlanetSystem _planets = default!;
    [Dependency] private readonly SWGlobalWarMarketSystem _markets = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly ActionBlockerSystem _blocker = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    private readonly Dictionary<SWGlobalWarFaction, List<SWGlobalWarReconReport>> _reports = new();
    private readonly Dictionary<(EntityUid Device, EntityUid Actor), Guid> _subscriptions = new();
    private readonly Dictionary<EntityUid, TimeSpan> _nextScan = new();
    private TimeSpan _nextUpdate;

    public override void Initialize()
    {
        SubscribeLocalEvent<SWGlobalWarCampaignStartedEvent>((ref SWGlobalWarCampaignStartedEvent _) => Reset());
        SubscribeLocalEvent<SWGlobalWarCampaignStoppedEvent>((ref SWGlobalWarCampaignStoppedEvent _) => Reset());
        SubscribeLocalEvent<SWGlobalWarReconDatapadComponent, AfterInteractEvent>(OnScan);
        SubscribeLocalEvent<SWGlobalWarReconDatapadComponent, BoundUIClosedEvent>((Entity<SWGlobalWarReconDatapadComponent> ent,
            ref BoundUIClosedEvent ev) => _subscriptions.Remove((ent.Owner, ev.Actor)));
        Subs.BuiEvents<SWGlobalWarReconDatapadComponent>(SWGlobalWarReconUiKey.Key,
            subs => subs.Event<SWGlobalWarReconOpen>(OnOpen));
    }

    private void Reset()
    {
        _reports.Clear();
        _nextScan.Clear();
        _subscriptions.Clear();
    }

    private bool Actor(EntityUid body, Guid generation, out SWGlobalWarAssignment assignment)
    {
        assignment = default!;
        return TryComp<ActorComponent>(body, out var actor) &&
               _assignments.TryGetAssignment(actor.PlayerSession.UserId, out assignment) &&
               _assignments.TryGetActor(actor.PlayerSession, generation, assignment.Revision, out assignment);
    }

    private void OnScan(EntityUid uid, SWGlobalWarReconDatapadComponent component, AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target ||
            !TryObserve(uid, args.User, target)) return;
        args.Handled = true;
        _popup.PopupEntity(Loc.GetString("sw-global-war-recon-saved"), uid, args.User);
        SendView(uid, args.User);
    }

    /// <summary>Records one nearby device and visible defenders; a stolen datapad does not grant the intelligence role.</summary>
    public bool TryObserve(EntityUid datapad, EntityUid body, EntityUid target)
    {
        if (!HasComp<SWGlobalWarReconDatapadComponent>(datapad) || _steps.IsClosing ||
            !_campaign.TryGetCampaign(out var campaign) || campaign.Comp.Phase != SWGlobalWarCampaignPhase.Running ||
            !Actor(body, campaign.Comp.Generation, out var assignment) || assignment.Role != SWGlobalWarRole.Intelligence ||
            _nextScan.TryGetValue(body, out var next) && next > _timing.CurTime ||
            !_blocker.CanInteract(body, target) || !_interaction.InRangeUnobstructed(body, target, range: 2f) ||
            !_interaction.InRangeUnobstructed(body, datapad, range: 2f) ||
            !_planets.TryGetPhysicalLocation(target, out var planet, out _) ||
            !HasComp<ApcPowerReceiverComponent>(target) || !this.IsPowered(target, EntityManager))
            return false;
        var definition = _prototypes.Index(Comp<SWGlobalWarPlanetComponent>(planet).Prototype);
        var lines = new List<string> { Loc.GetString("sw-global-war-recon-operational") };
        if (HasComp<SWGlobalWarMarketConsoleComponent>(target) && _markets.Simulation is { } simulation &&
            simulation.Generation == campaign.Comp.Generation && simulation.Markets.TryGetValue(definition.ID, out var market))
        {
            foreach (var (commodity, good) in market.Goods.OrderBy(p => p.Key).Take(16))
                lines.Add(Loc.GetString("sw-global-war-recon-stock",
                    ("commodity", Loc.GetString(_prototypes.Index<SWGlobalWarCommodityPrototype>(commodity).Name)),
                    ("quantity", Math.Round((double) good.Stock / simulation.UnitsPerItem, 1))));
        }
        if (HasComp<SWGlobalWarHubComponent>(target) && _markets.Simulation is { } freight)
        {
            // Only the observed hub's next outbound legs, never distant warehouses, budgets or complete routes.
            foreach (var destination in freight.Shipments.Where(s => s.Leg < s.Route.Length - 1 &&
                         s.Route[s.Leg] == definition.ID).Select(s => s.Route[s.Leg + 1]).Distinct().Take(8))
                lines.Add(Loc.GetString("sw-global-war-recon-route",
                    ("destination", _prototypes.Index<SWGlobalWarPlanetPrototype>(destination).Name)));
        }
        if (TryComp<SWGlobalWarFactoryComponent>(target, out var factory) && factory.Factory)
            lines.Add(Loc.GetString(factory.Enabled ? "sw-global-war-recon-factory-running" : "sw-global-war-recon-factory-idle"));
        var droids = 0;
        var guns = 0;
        var query = EntityQueryEnumerator<TransformComponent>();
        while (query.MoveNext(out var seen, out var transform))
        {
            var isDroid = HasComp<SWDroidCommandableComponent>(seen);
            var isGun = HasComp<SWAutoTargetingGunComponent>(seen);
            if (!isDroid && !isGun) continue;
            if (transform.MapUid != Transform(body).MapUid || transform.GridUid != transform.ParentUid ||
                !_interaction.InRangeUnobstructed(body, seen, range: 8f)) continue;
            if (isDroid) droids++;
            else if (isGun) guns++;
        }
        lines.Add(Loc.GetString("sw-global-war-recon-defenders", ("droids", droids), ("guns", guns)));
        if (!_reports.TryGetValue(assignment.Faction, out var reports))
            _reports.Add(assignment.Faction, reports = new List<SWGlobalWarReconReport>());
        reports.Add(new SWGlobalWarReconReport(Guid.NewGuid(), definition.Name, Name(target),
            _campaign.Elapsed(campaign), lines.ToArray()));
        if (reports.Count > 32) reports.RemoveAt(0);
        _nextScan[body] = _timing.CurTime + TimeSpan.FromSeconds(10);
        return true;
    }

    private void OnOpen(Entity<SWGlobalWarReconDatapadComponent> ent, ref SWGlobalWarReconOpen args)
    {
        if (args.Subscription == Guid.Empty || !_ui.IsUiOpen(ent.Owner, SWGlobalWarReconUiKey.Key, args.Actor)) return;
        _subscriptions[(ent.Owner, args.Actor)] = args.Subscription;
        SendView(ent.Owner, args.Actor);
    }

    /// <summary>Copies dated observations only for a currently authorized reader of the same faction.</summary>
    public SWGlobalWarReconReport[] GetReports(EntityUid body, out bool authorized)
    {
        authorized = _campaign.TryGetCampaign(out var campaign) &&
            Actor(body, campaign.Comp.Generation, out var assignment) &&
            assignment.Role is SWGlobalWarRole.Intelligence or SWGlobalWarRole.Head or
                SWGlobalWarRole.WarMinister or SWGlobalWarRole.SeniorOfficer;
        if (!authorized || !Actor(body, campaign.Comp.Generation, out var reader) ||
            !_reports.TryGetValue(reader.Faction, out var known)) return [];
        return known.AsEnumerable().Reverse().ToArray();
    }

    private void SendView(EntityUid device, EntityUid body)
    {
        if (!_subscriptions.TryGetValue((device, body), out var subscription)) return;
        if (TerminatingOrDeleted(device) || !_ui.IsUiOpen(device, SWGlobalWarReconUiKey.Key, body))
        { _subscriptions.Remove((device, body)); return; }
        var generation = Guid.Empty;
        var time = TimeSpan.Zero;
        var authorized = false;
        SWGlobalWarReconReport[] reports = [];
        if (_campaign.TryGetCampaign(out var campaign))
        {
            generation = campaign.Comp.Generation;
            time = _campaign.Elapsed(campaign);
            reports = GetReports(body, out authorized);
        }
        _ui.ServerSendUiMessage(device, SWGlobalWarReconUiKey.Key,
            new SWGlobalWarReconSnapshot(generation, subscription, GetNetEntity(body), time, authorized, reports), body);
    }

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _nextUpdate) return;
        _nextUpdate = _timing.CurTime + TimeSpan.FromSeconds(1);
        foreach (var ((device, actor), _) in _subscriptions.ToArray()) SendView(device, actor);
    }
}
