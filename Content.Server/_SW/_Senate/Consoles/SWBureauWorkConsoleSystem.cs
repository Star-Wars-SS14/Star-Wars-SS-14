using Content.Server.Popups;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Ministries;
using Content.Shared.Access.Systems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Content.Server._SW._Senate.Core;
using Content.Server._SW._Senate.Economy;

namespace Content.Server._SW._Senate.Consoles;

public sealed class SWBureauWorkConsoleSystem : EntitySystem
{
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly SharedIdCardSystem _idCard = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly SWMinistrySystem _ministry = default!;
    [Dependency] private readonly SWSenateDilemmaSystem _dilemma = default!;
    [Dependency] private readonly SWBureauShipsSystem _ships = default!;
    [Dependency] private readonly SWBureauTransitSystem _transit = default!;
    [Dependency] private readonly SWCorruptionSystem _corruption = default!;

    private const float UpdateInterval = 3f;
    private float _accumulator;

    public override void Initialize()
    {
        SubscribeLocalEvent<SWBureauWorkConsoleComponent, BoundUIOpenedEvent>(OnOpened);
        SubscribeLocalEvent<SWBureauWorkConsoleComponent, EntInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<SWBureauWorkConsoleComponent, EntRemovedFromContainerMessage>(OnRemoved);
        SubscribeLocalEvent<SWBureauWorkConsoleComponent, SWBureauResignMessage>(OnResign);
        SubscribeLocalEvent<SWBureauWorkConsoleComponent, SWBureauResolveDilemmaMessage>(OnResolveDilemma);
        SubscribeLocalEvent<SWBureauWorkConsoleComponent, SWShipsProcessAppMessage>(OnProcessApp);
        SubscribeLocalEvent<SWBureauWorkConsoleComponent, SWShipsSetPolicyMessage>(OnSetPolicy);
        SubscribeLocalEvent<SWBureauWorkConsoleComponent, SWShipsLaunchOpMessage>(OnLaunchOp);
        SubscribeLocalEvent<SWBureauWorkConsoleComponent, SWTransitSetSectorMessage>(OnSetTransitStatus);
        SubscribeLocalEvent<SWBureauWorkConsoleComponent, SWTransitResolveEventMessage>(OnResolveTransitEvent);
        SubscribeLocalEvent<SWBureauWorkConsoleComponent, SWMinistryAuditRequestMessage>(OnAudit);
        SubscribeLocalEvent<SWBureauWorkConsoleComponent, SWMinistryResolveRequestMessage>(OnResolveRequest);
        SubscribeLocalEvent<SWBureauWorkConsoleComponent, SWMinistryRefuseRequestMessage>(OnRefuseRequest);
    }

    private void OnAudit(Entity<SWBureauWorkConsoleComponent> ent, ref SWMinistryAuditRequestMessage args)
    {
        if (GetCardBureau(ent) is not { } bureauId)
            return;

        if (!_dilemma.AuditRequest(bureauId, args.RequestId))
            _popup.PopupCursor(Loc.GetString("sw-ministry-audit-failed"), args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnResolveRequest(Entity<SWBureauWorkConsoleComponent> ent, ref SWMinistryResolveRequestMessage args)
    {
        if (GetCardBureau(ent) is not { } bureauId)
            return;

        if (_dilemma.ResolveRequest(bureauId, args.RequestId, args.OptionIndex))
            _popup.PopupCursor(Loc.GetString("sw-ministry-request-resolved"), args.Actor, PopupType.Medium);
        else
            _popup.PopupCursor(Loc.GetString("sw-ministry-request-failed"), args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnRefuseRequest(Entity<SWBureauWorkConsoleComponent> ent, ref SWMinistryRefuseRequestMessage args)
    {
        if (GetCardBureau(ent) is not { } bureauId)
            return;

        if (_dilemma.RefuseRequest(bureauId, args.RequestId, args.Reason))
            _popup.PopupCursor(Loc.GetString("sw-ministry-request-refused"), args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private string? GetCardBureau(Entity<SWBureauWorkConsoleComponent> ent)
    {
        var card = _itemSlots.GetItemOrNull(ent.Owner, ent.Comp.IdSlot);
        if (card != null)
            _ministry.ReconcileCard(card.Value);
        if (card == null
            || !TryComp<SWSenatePositionCardComponent>(card, out var position)
            || position.Position != SWSenatePosition.Minister
            || !TryComp<SWMinistryComponent>(card, out var ministryComp)
            || ministryComp.Ministry is not { } id
            || !_proto.TryIndex<SWMinistryPrototype>(id, out var proto)
            || proto.Kind != SWMinistryKind.Bureau)
            return null;

        return id;
    }

    private void OnResign(Entity<SWBureauWorkConsoleComponent> ent, ref SWBureauResignMessage args)
    {
        var card = _itemSlots.GetItemOrNull(ent.Owner, ent.Comp.IdSlot);
        if (card == null
            || !TryComp<SWMinistryComponent>(card, out var ministryComp)
            || ministryComp.Ministry is not { } ministryId)
            return;

        _ministry.Unclaim(ministryId);
        ministryComp.Ministry = null;
        _idCard.TryChangeJobTitle(card.Value, Loc.GetString("sw-minister-base"));
        _popup.PopupCursor(Loc.GetString("sw-ministry-resigned"), args.Actor, PopupType.Medium);
        UpdateUi(ent);
    }

    private void OnResolveDilemma(Entity<SWBureauWorkConsoleComponent> ent, ref SWBureauResolveDilemmaMessage args)
    {
        switch (GetCardBureau(ent))
        {
            case SWBureauShipsSystem.BureauId:
                _ships.ResolveDilemma(args.OptionIndex);
                break;
            case SWBureauTransitSystem.BureauId:
                _transit.ResolveDilemma(args.OptionIndex);
                break;
            default:
                return;
        }

        UpdateUi(ent);
    }

    private void OnSetTransitStatus(Entity<SWBureauWorkConsoleComponent> ent, ref SWTransitSetSectorMessage args)
    {
        if (GetCardBureau(ent) != SWBureauTransitSystem.BureauId)
            return;

        _transit.SetStatus(args.SectorId, args.Status);
        UpdateUi(ent);
    }

    private void OnResolveTransitEvent(Entity<SWBureauWorkConsoleComponent> ent, ref SWTransitResolveEventMessage args)
    {
        if (GetCardBureau(ent) != SWBureauTransitSystem.BureauId)
            return;

        if (!_transit.ResolveEvent(args.EventId, args.OptionIndex))
            _popup.PopupCursor(Loc.GetString("sw-bureau-action-failed"), args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnProcessApp(Entity<SWBureauWorkConsoleComponent> ent, ref SWShipsProcessAppMessage args)
    {
        if (GetCardBureau(ent) != SWBureauShipsSystem.BureauId)
            return;

        if (!_ships.Process(args.AppId, args.Action))
            _popup.PopupCursor(Loc.GetString("sw-bureau-ships-action-failed"), args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnSetPolicy(Entity<SWBureauWorkConsoleComponent> ent, ref SWShipsSetPolicyMessage args)
    {
        if (GetCardBureau(ent) != SWBureauShipsSystem.BureauId)
            return;

        _ships.SetPolicy(args.Level);
        UpdateUi(ent);
    }

    private void OnLaunchOp(Entity<SWBureauWorkConsoleComponent> ent, ref SWShipsLaunchOpMessage args)
    {
        var ok = GetCardBureau(ent) switch
        {
            SWBureauShipsSystem.BureauId => _ships.LaunchOp(args.Op),
            SWBureauTransitSystem.BureauId => _transit.LaunchOp(args.Op),
            _ => (bool?) null,
        };

        if (ok == null)
            return;
        if (!ok.Value)
            _popup.PopupCursor(Loc.GetString("sw-bureau-action-failed"), args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnOpened(EntityUid uid, SWBureauWorkConsoleComponent comp, BoundUIOpenedEvent args)
    {
        UpdateUi((uid, comp));
    }

    private void OnInserted(Entity<SWBureauWorkConsoleComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID == ent.Comp.IdSlot)
            UpdateUi(ent);
    }

    private void OnRemoved(Entity<SWBureauWorkConsoleComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID == ent.Comp.IdSlot)
            UpdateUi(ent);
    }

    public override void Update(float frameTime)
    {
        _accumulator += frameTime;
        if (_accumulator < UpdateInterval)
            return;

        _accumulator = 0f;

        var query = EntityQueryEnumerator<SWBureauWorkConsoleComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (_ui.IsUiOpen(uid, SWBureauWorkConsoleUiKey.Key))
                UpdateUi((uid, comp));
        }
    }

    private void UpdateUi(Entity<SWBureauWorkConsoleComponent> ent)
    {
        var card = _itemSlots.GetItemOrNull(ent.Owner, ent.Comp.IdSlot);
        if (card != null)
            _ministry.ReconcileCard(card.Value);

        var state = new SWBureauWorkConsoleState
        {
            HasCard = card != null,
            GameTime = _time.TimeString(),
        };

        var bureauId = GetCardBureau(ent);
        if (bureauId != null && _proto.TryIndex<SWMinistryPrototype>(bureauId, out var proto))
        {
            state.IsBureauCard = true;
            state.HasBureau = true;
            state.CanResign = true;
            state.BureauId = bureauId;
            state.BureauName = proto.Name;
            state.BureauTitle = Loc.GetString("sw-ministry-title-bureau", ("name", proto.TitleName));
            state.BureauDescription = proto.Description;
            state.BureauColor = proto.Color;
            state.Budget = _dilemma.GetMinistryBudget(bureauId);
            state.Admin = _dilemma.GetMinistryAdmin(bureauId);
            state.AuditCost = _dilemma.AuditCost;
            state.Requests = _dilemma.BuildMinistryRequests(bureauId);

            if (_ministry.GetHolder(bureauId) is { } holder)
            {
                state.HasPersonal = true;
                state.MyCorruption = _corruption.PersonCorruptionInt(holder);
                state.MyAccount = _corruption.Account(holder);
            }

            if (bureauId == SWBureauShipsSystem.BureauId)
            {
                state.IsShips = true;
                state.TradeFlow = _ships.TradeFlow;
                state.TradeFlowStatus = _ships.TradeFlowStatus();
                state.Integrity = _ships.Integrity;
                state.IntegrityStatus = _ships.IntegrityStatus();
                state.Network = _ships.Network;
                state.NetworkStatus = _ships.NetworkStatus();
                state.Policy = _ships.Policy;
                state.PolicyLabel = SWBureauShipsSystem.PolicyLabel(_ships.Policy);
                state.ForgedActive = _ships.ForgedTotal;
                state.Applications = _ships.BuildApps();
                state.Dilemma = _ships.BuildDilemma();
                state.Fleet = _ships.BuildFleet();
                state.Wanted = _ships.BuildWanted();
                state.Ops = _ships.BuildOps();
                state.RegistryLog = _ships.GetLog();
            }
            else if (bureauId == SWBureauTransitSystem.BureauId)
            {
                state.IsTransit = true;
                state.Mobility = _transit.Mobility;
                state.MobilityStatus = _transit.MobilityStatus();
                state.Congestion = _transit.Congestion;
                state.CongestionStatus = _transit.CongestionStatus();
                state.LineSafety = _transit.Safety;
                state.LineSafetyStatus = _transit.SafetyStatus();
                state.TicketIncome = _transit.TicketIncome();
                state.TransitSectors = _transit.BuildSectors();
                state.TransitEvents = _transit.BuildEvents();
                state.Dilemma = _transit.BuildDilemma();
                state.Ops = _transit.BuildOps();
                state.RegistryLog = _transit.GetLog();
            }
        }
        else if (card != null && TryComp<SWSenatePositionCardComponent>(card, out var position) && position.Position == SWSenatePosition.Minister)
        {
            state.IsBureauCard = true;
        }

        _ui.SetUiState(ent.Owner, SWBureauWorkConsoleUiKey.Key, state);
    }
}
