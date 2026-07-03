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

public sealed class SWDepartmentWorkConsoleSystem : EntitySystem
{
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly SharedIdCardSystem _idCard = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly SWMinistrySystem _ministry = default!;
    [Dependency] private readonly SWSenateDilemmaSystem _dilemma = default!;
    [Dependency] private readonly SWCorruptionSystem _corruption = default!;
    [Dependency] private readonly SWDeptHealthcareSystem _healthcare = default!;
    [Dependency] private readonly SWDeptGuardSystem _guard = default!;
    [Dependency] private readonly SWDeptArbitrageSystem _arbitrage = default!;
    [Dependency] private readonly SWDeptExpenditureSystem _expenditure = default!;
    [Dependency] private readonly SWSenateAmbitionSystem _ambition = default!;

    private const float UpdateInterval = 3f;
    private float _accumulator;

    public override void Initialize()
    {
        SubscribeLocalEvent<SWDepartmentWorkConsoleComponent, BoundUIOpenedEvent>(OnOpened);
        SubscribeLocalEvent<SWDepartmentWorkConsoleComponent, EntInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<SWDepartmentWorkConsoleComponent, EntRemovedFromContainerMessage>(OnRemoved);
        SubscribeLocalEvent<SWDepartmentWorkConsoleComponent, SWBureauResignMessage>(OnResign);
        SubscribeLocalEvent<SWDepartmentWorkConsoleComponent, SWShipsLaunchOpMessage>(OnLaunchOp);
        SubscribeLocalEvent<SWDepartmentWorkConsoleComponent, SWDeptResolveTaskMessage>(OnResolveTask);
        SubscribeLocalEvent<SWDepartmentWorkConsoleComponent, SWDeptCaseAdvanceMessage>(OnCaseAdvance);
        SubscribeLocalEvent<SWDepartmentWorkConsoleComponent, SWDeptAuditTargetMessage>(OnAuditTarget);
        SubscribeLocalEvent<SWDepartmentWorkConsoleComponent, SWDeptPostMessage>(OnPost);
        SubscribeLocalEvent<SWDepartmentWorkConsoleComponent, SWMinistryAuditRequestMessage>(OnAuditRequest);
        SubscribeLocalEvent<SWDepartmentWorkConsoleComponent, SWMinistryResolveRequestMessage>(OnResolveRequest);
        SubscribeLocalEvent<SWDepartmentWorkConsoleComponent, SWMinistryRefuseRequestMessage>(OnRefuseRequest);
    }

    private string? GetCardDepartment(Entity<SWDepartmentWorkConsoleComponent> ent)
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
            || proto.Kind != SWMinistryKind.Department)
            return null;

        return id;
    }

    private void OnResign(Entity<SWDepartmentWorkConsoleComponent> ent, ref SWBureauResignMessage args)
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

    private void OnLaunchOp(Entity<SWDepartmentWorkConsoleComponent> ent, ref SWShipsLaunchOpMessage args)
    {
        var ok = GetCardDepartment(ent) switch
        {
            SWDeptHealthcareSystem.DeptId => _healthcare.LaunchOp(args.Op),
            SWDeptGuardSystem.DeptId => _guard.LaunchOp(args.Op),
            SWDeptArbitrageSystem.DeptId => _arbitrage.LaunchOp(args.Op),
            SWDeptExpenditureSystem.DeptId => _expenditure.LaunchOp(args.Op),
            _ => (bool?) null,
        };

        if (ok == null)
            return;
        if (!ok.Value)
            _popup.PopupCursor(Loc.GetString("sw-bureau-action-failed"), args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnResolveTask(Entity<SWDepartmentWorkConsoleComponent> ent, ref SWDeptResolveTaskMessage args)
    {
        var ok = GetCardDepartment(ent) switch
        {
            SWDeptHealthcareSystem.DeptId => _healthcare.ResolveCall(args.TaskId, args.OptionIndex),
            SWDeptGuardSystem.DeptId => _guard.ResolveWrit(args.TaskId, args.OptionIndex),
            SWDeptExpenditureSystem.DeptId => _expenditure.ResolveTip(args.TaskId, args.OptionIndex),
            _ => (bool?) null,
        };

        if (ok == null)
            return;
        if (!ok.Value)
            _popup.PopupCursor(Loc.GetString("sw-bureau-action-failed"), args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnCaseAdvance(Entity<SWDepartmentWorkConsoleComponent> ent, ref SWDeptCaseAdvanceMessage args)
    {
        if (GetCardDepartment(ent) != SWDeptArbitrageSystem.DeptId)
            return;

        if (!_arbitrage.AdvanceCase(args.CaseId))
            _popup.PopupCursor(Loc.GetString("sw-bureau-action-failed"), args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnAuditTarget(Entity<SWDepartmentWorkConsoleComponent> ent, ref SWDeptAuditTargetMessage args)
    {
        if (GetCardDepartment(ent) != SWDeptExpenditureSystem.DeptId)
            return;

        if (!_expenditure.AuditTarget(args.TargetId))
            _popup.PopupCursor(Loc.GetString("sw-bureau-action-failed"), args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnPost(Entity<SWDepartmentWorkConsoleComponent> ent, ref SWDeptPostMessage args)
    {
        if (GetCardDepartment(ent) != SWDeptGuardSystem.DeptId)
            return;

        _guard.SetPost(args.Post, args.Delta);
        UpdateUi(ent);
    }

    private void OnAuditRequest(Entity<SWDepartmentWorkConsoleComponent> ent, ref SWMinistryAuditRequestMessage args)
    {
        if (GetCardDepartment(ent) is not { } deptId)
            return;

        if (!_dilemma.AuditRequest(deptId, args.RequestId))
            _popup.PopupCursor(Loc.GetString("sw-ministry-audit-failed"), args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnResolveRequest(Entity<SWDepartmentWorkConsoleComponent> ent, ref SWMinistryResolveRequestMessage args)
    {
        if (GetCardDepartment(ent) is not { } deptId)
            return;

        if (_dilemma.ResolveRequest(deptId, args.RequestId, args.OptionIndex))
            _popup.PopupCursor(Loc.GetString("sw-ministry-request-resolved"), args.Actor, PopupType.Medium);
        else
            _popup.PopupCursor(Loc.GetString("sw-ministry-request-failed"), args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnRefuseRequest(Entity<SWDepartmentWorkConsoleComponent> ent, ref SWMinistryRefuseRequestMessage args)
    {
        if (GetCardDepartment(ent) is not { } deptId)
            return;

        if (_dilemma.RefuseRequest(deptId, args.RequestId, args.Reason))
            _popup.PopupCursor(Loc.GetString("sw-ministry-request-refused"), args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnOpened(EntityUid uid, SWDepartmentWorkConsoleComponent comp, BoundUIOpenedEvent args)
    {
        UpdateUi((uid, comp));
    }

    private void OnInserted(Entity<SWDepartmentWorkConsoleComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID == ent.Comp.IdSlot)
            UpdateUi(ent);
    }

    private void OnRemoved(Entity<SWDepartmentWorkConsoleComponent> ent, ref EntRemovedFromContainerMessage args)
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

        var query = EntityQueryEnumerator<SWDepartmentWorkConsoleComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (_ui.IsUiOpen(uid, SWDepartmentWorkConsoleUiKey.Key))
                UpdateUi((uid, comp));
        }
    }

    private void UpdateUi(Entity<SWDepartmentWorkConsoleComponent> ent)
    {
        var card = _itemSlots.GetItemOrNull(ent.Owner, ent.Comp.IdSlot);
        if (card != null)
            _ministry.ReconcileCard(card.Value);

        var state = new SWDepartmentWorkConsoleState
        {
            HasCard = card != null,
            GameTime = _time.TimeString(),
        };

        var deptId = GetCardDepartment(ent);
        if (deptId != null && _proto.TryIndex<SWMinistryPrototype>(deptId, out var proto))
        {
            state.IsDeptCard = true;
            state.HasDept = true;
            state.CanResign = true;
            state.DeptId = deptId;
            state.DeptName = proto.Name;
            state.DeptDescription = proto.Description;
            state.DeptColor = proto.Color;
            state.Budget = _dilemma.GetMinistryBudget(deptId);
            state.Admin = _dilemma.GetMinistryAdmin(deptId);
            state.AuditCost = _dilemma.AuditCost;
            state.Requests = _dilemma.BuildMinistryRequests(deptId);
            state.Ambitions = _ambition.BuildAmbitions(deptId);

            if (proto.Parent is { } parentId && _proto.TryIndex<SWMinistryPrototype>(parentId, out var parentProto))
            {
                state.ParentName = parentProto.Name;
                state.ParentActive = _ministry.IsClaimed(parentId);
            }

            if (_ministry.GetHolder(deptId) is { } holder)
            {
                state.HasPersonal = true;
                state.MyCorruption = _corruption.PersonCorruptionInt(holder);
                state.MyAccount = _corruption.Account(holder);
            }

            switch (deptId)
            {
                case SWDeptHealthcareSystem.DeptId:
                    state.IsHealthcare = true;
                    state.FreeBrigades = _healthcare.FreeBrigades;
                    state.TotalBrigades = 3;
                    state.ActiveMissions = _healthcare.ActiveMissions;
                    state.Meters.Add(new SWDeptMeterEntry { Label = "Готовность бригад", Value = _healthcare.Readiness, Status = _healthcare.ReadinessStatus() });
                    state.Meters.Add(new SWDeptMeterEntry { Label = "Запасы медикаментов", Value = _healthcare.Supplies, Status = _healthcare.SuppliesStatus() });
                    state.Tasks = _healthcare.BuildCalls();
                    state.Ops = _healthcare.BuildOps();
                    state.Log = _healthcare.GetLog();
                    break;

                case SWDeptGuardSystem.DeptId:
                    state.IsGuard = true;
                    state.ReserveSquads = _guard.ReserveSquads;
                    state.TotalSquads = _guard.TotalSquads;
                    state.Posts = _guard.BuildPosts();
                    state.Meters.Add(new SWDeptMeterEntry { Label = "Дисциплина", Value = _guard.Discipline, Status = _guard.DisciplineStatus() });
                    state.Tasks = _guard.BuildWrits();
                    state.Ops = _guard.BuildOps();
                    state.Log = _guard.GetLog();
                    break;

                case SWDeptArbitrageSystem.DeptId:
                    state.IsArbitrage = true;
                    state.Solved = _arbitrage.Solved;
                    state.Cases = _arbitrage.BuildCases();
                    state.Meters.Add(new SWDeptMeterEntry { Label = "Независимость", Value = _arbitrage.Independence, Status = _arbitrage.IndependenceStatus() });
                    state.Ops = _arbitrage.BuildOps();
                    state.Log = _arbitrage.GetLog();
                    break;

                case SWDeptExpenditureSystem.DeptId:
                    state.IsExpenditure = true;
                    state.Recovered = _expenditure.Recovered;
                    state.AuditRows = _expenditure.BuildLedger();
                    state.Meters.Add(new SWDeptMeterEntry { Label = "Прозрачность бюджета", Value = _expenditure.Transparency, Status = _expenditure.TransparencyStatus() });
                    state.Tasks = _expenditure.BuildTips();
                    state.Ops = _expenditure.BuildOps();
                    state.Log = _expenditure.GetLog();
                    break;
            }
        }
        else if (card != null && TryComp<SWSenatePositionCardComponent>(card, out var position) && position.Position == SWSenatePosition.Minister)
        {
            state.IsDeptCard = true;
        }

        _ui.SetUiState(ent.Owner, SWDepartmentWorkConsoleUiKey.Key, state);
    }
}
