using System;
using System.Linq;
using Content.Server.Popups;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Ministries;
using Content.Shared._SW._Senate.Sectors;
using Content.Shared.Access.Systems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Content.Server._SW._Senate.Core;
using Content.Server._SW._Senate.Economy;

namespace Content.Server._SW._Senate.Consoles;

public sealed class SWMinistryWorkConsoleSystem : EntitySystem
{
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly SWSenateStateSystem _senate = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly SWSenateDilemmaSystem _dilemma = default!;
    [Dependency] private readonly SWMinistryDefenseSystem _defense = default!;
    [Dependency] private readonly SWMinistrySystem _ministry = default!;
    [Dependency] private readonly SharedIdCardSystem _idCard = default!;
    [Dependency] private readonly PopupSystem _popup = default!;

    private const float UpdateInterval = 3f;
    private float _accumulator;

    public override void Initialize()
    {
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, BoundUIOpenedEvent>(OnOpened);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, EntInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, EntRemovedFromContainerMessage>(OnRemoved);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWMinistryAuditRequestMessage>(OnAudit);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWMinistryResolveRequestMessage>(OnResolve);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWMinistryRefuseRequestMessage>(OnRefuse);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWDefenseSetStanceMessage>(OnSetStance);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWDefenseSelectProgramMessage>(OnSelectProgram);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWFleetDeployMessage>(OnDeployFleet);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWDefenseLaunchOpMessage>(OnLaunchOp);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWMinistryResignMessage>(OnResign);
    }

    private void OnLaunchOp(Entity<SWMinistryWorkConsoleComponent> ent, ref SWDefenseLaunchOpMessage args)
    {
        if (GetCardMinistry(ent) != SWMinistryDefenseSystem.DefenseMinistryId)
            return;

        if (!_defense.LaunchOperation(args.OpId, args.Target))
            _popup.PopupCursor(Loc.GetString("sw-defense-op-failed"), args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnDeployFleet(Entity<SWMinistryWorkConsoleComponent> ent, ref SWFleetDeployMessage args)
    {
        if (GetCardMinistry(ent) != SWMinistryDefenseSystem.DefenseMinistryId)
            return;

        _defense.DeployGroup(args.GroupId, args.Target);
        UpdateUi(ent);
    }

    private void OnResign(Entity<SWMinistryWorkConsoleComponent> ent, ref SWMinistryResignMessage args)
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

    private void OnSetStance(Entity<SWMinistryWorkConsoleComponent> ent, ref SWDefenseSetStanceMessage args)
    {
        if (GetCardMinistry(ent) != SWMinistryDefenseSystem.DefenseMinistryId)
            return;

        _defense.SetStance(args.Stance);
        UpdateUi(ent);
    }

    private void OnSelectProgram(Entity<SWMinistryWorkConsoleComponent> ent, ref SWDefenseSelectProgramMessage args)
    {
        if (GetCardMinistry(ent) != SWMinistryDefenseSystem.DefenseMinistryId)
            return;

        _defense.SelectProgram(args.ProgramId);
        UpdateUi(ent);
    }

    private string? GetCardMinistry(Entity<SWMinistryWorkConsoleComponent> ent)
    {
        var card = _itemSlots.GetItemOrNull(ent.Owner, ent.Comp.IdSlot);
        if (card == null
            || !TryComp<SWSenatePositionCardComponent>(card, out var position)
            || position.Position != SWSenatePosition.Minister
            || !TryComp<SWMinistryComponent>(card, out var ministryComp)
            || ministryComp.Ministry == null)
            return null;

        return ministryComp.Ministry;
    }

    private void OnAudit(Entity<SWMinistryWorkConsoleComponent> ent, ref SWMinistryAuditRequestMessage args)
    {
        if (GetCardMinistry(ent) is not { } ministryId)
            return;

        if (!_dilemma.AuditRequest(ministryId, args.RequestId))
            _popup.PopupCursor(Loc.GetString("sw-ministry-audit-failed"), args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnResolve(Entity<SWMinistryWorkConsoleComponent> ent, ref SWMinistryResolveRequestMessage args)
    {
        if (GetCardMinistry(ent) is not { } ministryId)
            return;

        if (_dilemma.ResolveRequest(ministryId, args.RequestId, args.OptionIndex))
            _popup.PopupCursor(Loc.GetString("sw-ministry-request-resolved"), args.Actor, PopupType.Medium);
        else
            _popup.PopupCursor(Loc.GetString("sw-ministry-request-failed"), args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnRefuse(Entity<SWMinistryWorkConsoleComponent> ent, ref SWMinistryRefuseRequestMessage args)
    {
        if (GetCardMinistry(ent) is not { } ministryId)
            return;

        if (_dilemma.RefuseRequest(ministryId, args.RequestId, args.Reason))
            _popup.PopupCursor(Loc.GetString("sw-ministry-request-refused"), args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnOpened(EntityUid uid, SWMinistryWorkConsoleComponent comp, BoundUIOpenedEvent args)
    {
        UpdateUi((uid, comp));
    }

    private void OnInserted(Entity<SWMinistryWorkConsoleComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID == ent.Comp.IdSlot)
            UpdateUi(ent);
    }

    private void OnRemoved(Entity<SWMinistryWorkConsoleComponent> ent, ref EntRemovedFromContainerMessage args)
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

        var query = EntityQueryEnumerator<SWMinistryWorkConsoleComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (_ui.IsUiOpen(uid, SWMinistryWorkConsoleUiKey.Key))
                UpdateUi((uid, comp));
        }
    }

    private void UpdateUi(Entity<SWMinistryWorkConsoleComponent> ent)
    {
        var card = _itemSlots.GetItemOrNull(ent.Owner, ent.Comp.IdSlot);

        var state = new SWMinistryWorkConsoleState
        {
            HasCard = card != null,
            GameTime = _time.TimeString(),
            Globals = _senate.BuildGlobalEntries(),
        };

        if (card != null
            && TryComp<SWSenatePositionCardComponent>(card, out var position)
            && position.Position == SWSenatePosition.Minister)
        {
            state.IsMinisterCard = true;

            if (TryComp<SWMinistryComponent>(card, out var ministryComp)
                && ministryComp.Ministry != null
                && _proto.TryIndex<SWMinistryPrototype>(ministryComp.Ministry, out var proto))
            {
                state.HasMinistry = true;
                state.CanResign = true;
                state.MinistryName = proto.Name;
                state.MinistryTitle = Loc.GetString(TitleKey(proto.Kind), ("name", proto.TitleName));
                state.MinistryDescription = proto.Description;
                state.MinistryColor = proto.Color;
                state.KindLabel = KindLabel(proto.Kind);
                state.DomainStatId = proto.Domain;

                var ministryId = ministryComp.Ministry.Value.Id;
                state.MinistryBudget = _dilemma.GetMinistryBudget(ministryId);
                state.MinistryAdmin = _dilemma.GetMinistryAdmin(ministryId);
                state.AuditCost = _dilemma.AuditCost;
                state.Requests = _dilemma.BuildMinistryRequests(ministryId);

                if (ministryId == SWMinistryDefenseSystem.DefenseMinistryId)
                {
                    state.IsDefense = true;
                    state.Stance = _defense.Stance;
                    state.DefconLevel = SWMinistryDefenseSystem.DefconLevel(_defense.Stance);
                    state.StanceName = SWMinistryDefenseSystem.StanceName(_defense.Stance);
                    state.StanceColor = SWMinistryDefenseSystem.StanceColor(_defense.Stance);
                    state.StanceEffects = _defense.BuildStanceEffects(_defense.Stance);
                    state.WarPending = _defense.WarPending;
                    state.PendingStanceName = _defense.PendingStance is { } pend ? SWMinistryDefenseSystem.StanceName(pend) : string.Empty;
                    state.BorderPressure = _defense.BorderPressure;
                    state.BorderStatus = _defense.BorderStatus();
                    state.WarWeariness = _defense.WarWeariness;
                    state.SelectedProgram = _defense.SelectedProgram ?? string.Empty;
                    state.ProgramProgress = _defense.ProgramProgress;
                    state.Programs = _defense.BuildPrograms();
                    state.Fleet = _defense.BuildFleet();
                    state.FleetUpkeep = _defense.FleetUpkeep;
                    state.FleetSectors = BuildClaimedSectors();
                    state.Operations = _defense.BuildOperations();
                    state.ActiveOps = _defense.BuildActiveOps();
                    state.ActiveOpSlots = _defense.ActiveOpSlots;
                    state.ActiveOpUsed = _defense.ActiveOpUsed;
                }
            }
        }

        _ui.SetUiState(ent.Owner, SWMinistryWorkConsoleUiKey.Key, state);
    }

    private List<SWFleetSectorEntry> BuildClaimedSectors()
    {
        var list = new List<SWFleetSectorEntry>();
        foreach (var (sectorId, _) in _senate.SectorHolders)
        {
            if (!_proto.TryIndex<SWSectorPrototype>(sectorId, out var proto))
                continue;

            var defense = _senate.SectorGlobals.TryGetValue(sectorId, out var globals)
                && globals.TryGetValue("SWGlobalDefense", out var d)
                    ? (int) MathF.Round(d)
                    : 50;

            list.Add(new SWFleetSectorEntry { Id = sectorId, Name = proto.Name, Color = proto.Color, Defense = defense });
        }

        list.Sort((a, b) => a.Defense.CompareTo(b.Defense));
        return list;
    }

    private static string TitleKey(SWMinistryKind kind) => kind switch
    {
        SWMinistryKind.Department => "sw-ministry-title-department",
        SWMinistryKind.Bureau => "sw-ministry-title-bureau",
        _ => "sw-ministry-title-ministry",
    };

    private static string KindLabel(SWMinistryKind kind) => kind switch
    {
        SWMinistryKind.Department => "Департамент",
        SWMinistryKind.Bureau => "Бюро",
        _ => "Министерство",
    };
}
