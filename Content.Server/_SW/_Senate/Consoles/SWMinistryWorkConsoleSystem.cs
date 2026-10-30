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

public sealed partial class SWMinistryWorkConsoleSystem : EntitySystem
{
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly SWSenateStateSystem _senate = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly SWSenateDilemmaSystem _dilemma = default!;
    [Dependency] private readonly SWMinistryDefenseSystem _defense = default!;
    [Dependency] private readonly SWMinistryInternalSecuritySystem _security = default!;
    [Dependency] private readonly SWMinistryHealthSystem _health = default!;
    [Dependency] private readonly Content.Server._SW._Senate.Economy.SWRepublicEconomyPublicationSystem _healthEconomy = default!;
    [Dependency] private readonly SWMinistryEconomySystem _economy = default!;
    [Dependency] private readonly SWStatisticsConsoleSystem _mapStatistics = default!;
    [Dependency] private readonly SWMinistryFinanceSystem _finance = default!;
    [Dependency] private readonly SWSenateBudgetSystem _budget = default!;
    [Dependency] private readonly Content.Server._SW._Senate.Economy.SWGovernorSystem _governor = default!;
    [Dependency] private readonly SWMinistryJusticeSystem _justice = default!;
    [Dependency] private readonly SWCorruptionSystem _corruption = default!;
    [Dependency] private readonly SWMinistrySystem _ministry = default!;
    [Dependency] private readonly SWSenateAmbitionSystem _ambition = default!;

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
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWFleetDeployMessage>(OnDeployFleet);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWDefenseLaunchOpMessage>(OnLaunchOp);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWDefenseFleetOrderMessage>(OnFleetOrder);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWDefenseRecruitMessage>(OnRecruitFleet);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWDefenseWorldProgramMessage>(OnWorldDefenseProgram);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWDefenseWorldResupplyMessage>(OnWorldDefenseResupply);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWSecuritySetPostureMessage>(OnSetPosture);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWSecurityTeamOrderMessage>(OnSecurityOrder);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWSecurityCellCommandMessage>(OnSecurityCell);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWHealthActionMessage>(OnHealthAction);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWEconomySetPolicyMessage>(OnSetEconPolicy);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWEconomySelectGoodMessage>(OnSelectEconGood);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWEconomyFundExpansionMessage>(OnFundEconExpansion);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWEconomyFundRouteMessage>(OnFundEconRoute);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWEconomyRespondEventMessage>(OnRespondEconomyEvent);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWEconomyDeclareStimulusMessage>(OnDeclareStimulus);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWFinanceSetRateMessage>(OnSetRate);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWFinanceSetTaxMessage>(OnSetTax);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWFinanceIssueBondsMessage>(OnIssueBonds);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWFinanceRepayDebtMessage>(OnRepayDebt);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWFinanceEmissionMessage>(OnEmission);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWBudgetAdjustRatioMessage>(OnBudgetRatio);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWBudgetSetWeightMessage>(OnBudgetWeight);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWBudgetResetWeightsMessage>(OnBudgetResetWeights);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWBudgetSubmitMessage>(OnBudgetSubmit);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWBudgetReserveTransferMessage>(OnBudgetReserveTransfer);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWFinanceTaxSetMessage>(OnFinanceTaxSet);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWFinanceGovernorDecisionMessage>(OnFinanceGovernorDecision);
        InitializeJusticeInspections();
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWMinistryResignMessage>(OnResign);
    }

    private bool IsJusticeCard(Entity<SWMinistryWorkConsoleComponent> ent) =>
        GetCardMinistry(ent) == SWMinistryJusticeSystem.JusticeMinistryId;

    private void OnBudgetRatio(Entity<SWMinistryWorkConsoleComponent> ent, ref SWBudgetAdjustRatioMessage args)
    {
        if (!_time.ModeActive || !_time.DayActive)
            return;

        if (!CanActAsFinance(ent, args.Actor))
            return;

        _budget.AdjustRatio(args.DeltaPct);
        UpdateUi(ent);
    }

    private void OnBudgetWeight(Entity<SWMinistryWorkConsoleComponent> ent, ref SWBudgetSetWeightMessage args)
    {
        if (!_time.ModeActive || !_time.DayActive)
            return;

        if (!CanActAsFinance(ent, args.Actor))
            return;

        _budget.SetWeight(args.MinistryId, args.Delta);
        UpdateUi(ent);
    }

    private void OnBudgetResetWeights(Entity<SWMinistryWorkConsoleComponent> ent, ref SWBudgetResetWeightsMessage args)
    {
        if (!_time.ModeActive || !_time.DayActive)
            return;

        if (!CanActAsFinance(ent, args.Actor))
            return;

        _budget.ResetWeights();
        UpdateUi(ent);
    }

    private void OnBudgetSubmit(Entity<SWMinistryWorkConsoleComponent> ent, ref SWBudgetSubmitMessage args)
    {
        if (!_time.ModeActive || !_time.DayActive)
            return;

        if (!CanActAsFinance(ent, args.Actor))
            return;

        if (_budget.Submit(args.Revision, args.SectorPct, args.ReservePct, args.RepublicPct, args.Weights))
            _popup.PopupCursor(Loc.GetString("sw-budget-submitted"), args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnBudgetReserveTransfer(Entity<SWMinistryWorkConsoleComponent> ent, ref SWBudgetReserveTransferMessage args)
    {
        if (!_time.ModeActive || !_time.DayActive || !CanActAsFinance(ent, args.Actor)) return;
        var success = _budget.Transfer(args.Revision, args.RequestId, args.Ministry, args.TargetId, args.Amount, args.Purpose);
        _budget.ReportTransfer(args.RequestId, success);
        UpdateUi(ent);
    }

    private void OnFinanceTaxSet(Entity<SWMinistryWorkConsoleComponent> ent, ref SWFinanceTaxSetMessage args)
    {
        if (!_time.ModeActive || !_time.DayActive || !CanActAsFinance(ent, args.Actor))
        {
            _finance.ReportTaxResult(args.RequestId, false);
            UpdateUi(ent);
            return;
        }
        var success = !string.IsNullOrWhiteSpace(args.RequestId) && args.RequestId.Length <= 100
            && _finance.SetCommodityTax(args.Id, args.Rate, args.Relief);
        _finance.ReportTaxResult(args.RequestId, success);
        UpdateUi(ent);
    }

    private void OnFinanceGovernorDecision(Entity<SWMinistryWorkConsoleComponent> ent, ref SWFinanceGovernorDecisionMessage args)
    {
        if (!_time.ModeActive || !_time.DayActive || !CanActAsFinance(ent, args.Actor))
        {
            _governor.ReportFinanceDenied(args.RequestId);
            UpdateUi(ent);
            return;
        }
        _governor.DecideFinanceRequest(args.RequestId, args.Approve);
        UpdateUi(ent);
    }

    private bool IsFinanceCard(Entity<SWMinistryWorkConsoleComponent> ent) =>
        GetCardMinistry(ent) == SWMinistryFinanceSystem.FinanceMinistryId;

    private bool CanActAsFinance(Entity<SWMinistryWorkConsoleComponent> ent, EntityUid actor)
    {
        if (IsFinanceCard(ent) && _ministry.IsActiveHolder(SWMinistryFinanceSystem.FinanceMinistryId, actor))
            return true;
        _popup.PopupCursor(Loc.GetString("sw-finance-holder-required"), actor, PopupType.Medium);
        return false;
    }

    private void OnSetRate(Entity<SWMinistryWorkConsoleComponent> ent, ref SWFinanceSetRateMessage args)
    {
        if (!_time.ModeActive || !_time.DayActive)
            return;

        if (!CanActAsFinance(ent, args.Actor))
            return;

        _finance.SetRate(args.Rate);
        UpdateUi(ent);
    }

    private void OnSetTax(Entity<SWMinistryWorkConsoleComponent> ent, ref SWFinanceSetTaxMessage args)
    {
        if (!_time.ModeActive || !_time.DayActive)
            return;

        if (!CanActAsFinance(ent, args.Actor))
            return;

        _finance.SetTax(args.Level);
        UpdateUi(ent);
    }

    private void OnIssueBonds(Entity<SWMinistryWorkConsoleComponent> ent, ref SWFinanceIssueBondsMessage args)
    {
        if (!_time.ModeActive || !_time.DayActive)
            return;

        if (!CanActAsFinance(ent, args.Actor))
            return;

        if (!_finance.IssueBonds())
            _popup.PopupCursor(Loc.GetString("sw-finance-bonds-failed"), args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnRepayDebt(Entity<SWMinistryWorkConsoleComponent> ent, ref SWFinanceRepayDebtMessage args)
    {
        if (!_time.ModeActive || !_time.DayActive)
            return;

        if (!CanActAsFinance(ent, args.Actor))
            return;

        if (!_finance.RepayDebt())
            _popup.PopupCursor(Loc.GetString("sw-finance-repay-failed"), args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnEmission(Entity<SWMinistryWorkConsoleComponent> ent, ref SWFinanceEmissionMessage args)
    {
        if (!_time.ModeActive || !_time.DayActive)
            return;

        if (!CanActAsFinance(ent, args.Actor))
            return;

        _finance.DeclareEmission();
        UpdateUi(ent);
    }

    private bool IsEconomyCard(Entity<SWMinistryWorkConsoleComponent> ent) =>
        GetCardMinistry(ent) == SWMinistryEconomySystem.EconomyMinistryId;

    private void OnSetEconPolicy(Entity<SWMinistryWorkConsoleComponent> ent, ref SWEconomySetPolicyMessage args)
    {
        if (!_time.ModeActive || !_time.DayActive)
            return;

        if (!IsEconomyCard(ent))
            return;

        _economy.SetPolicy(args.Level);
        UpdateUi(ent);
    }

    private void OnSelectEconGood(Entity<SWMinistryWorkConsoleComponent> ent, ref SWEconomySelectGoodMessage args)
    {
        if (!IsEconomyCard(ent) || !_time.ModeActive || !_time.DayActive) return;
        _economy.SelectGood(args.Good);
        UpdateUi(ent);
    }

    private void OnFundEconExpansion(Entity<SWMinistryWorkConsoleComponent> ent, ref SWEconomyFundExpansionMessage args)
    {
        if (!IsEconomyCard(ent) || !_time.ModeActive || !_time.DayActive) return;
        if (!_economy.FundExpansion(args.World, args.Recipe))
            _popup.PopupCursor(Loc.GetString("sw-economy-project-failed"), args.Actor, PopupType.Medium);
        UpdateUi(ent);
    }

    private void OnFundEconRoute(Entity<SWMinistryWorkConsoleComponent> ent, ref SWEconomyFundRouteMessage args)
    {
        if (!IsEconomyCard(ent) || !_time.ModeActive || !_time.DayActive) return;
        if (!_economy.FundRoute(args.Edge))
            _popup.PopupCursor(Loc.GetString("sw-economy-route-failed"), args.Actor, PopupType.Medium);
        UpdateUi(ent);
    }

    private void OnRespondEconomyEvent(Entity<SWMinistryWorkConsoleComponent> ent, ref SWEconomyRespondEventMessage args)
    {
        if (!IsEconomyCard(ent) || !_ministry.IsActiveHolder(SWMinistryEconomySystem.EconomyMinistryId, args.Actor)) return;
        if (!_economy.RespondEconomyEvent(args.EventId, args.Accelerated))
            _popup.PopupCursor(Loc.GetString("sw-economy-project-failed"), args.Actor, PopupType.Medium);
        UpdateUi(ent);
    }

    private void OnLaunchProject(Entity<SWMinistryWorkConsoleComponent> ent, ref SWEconomyLaunchProjectMessage args)
    {
        if (!_time.ModeActive || !_time.DayActive)
            return;

        if (!IsEconomyCard(ent))
            return;

        if (!_economy.LaunchProject(args.ProjectId, args.Target))
            _popup.PopupCursor(Loc.GetString("sw-economy-project-failed"), args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnCancelProject(Entity<SWMinistryWorkConsoleComponent> ent, ref SWEconomyCancelProjectMessage args)
    {
        if (!_time.ModeActive || !_time.DayActive)
            return;

        if (!IsEconomyCard(ent))
            return;

        _economy.CancelProject(args.Index);
        UpdateUi(ent);
    }

    private void OnAddRoute(Entity<SWMinistryWorkConsoleComponent> ent, ref SWEconomyAddRouteMessage args)
    {
        if (!_time.ModeActive || !_time.DayActive)
            return;

        if (!IsEconomyCard(ent))
            return;

        if (!_economy.AddRoute(args.AId, args.BId))
            _popup.PopupCursor(Loc.GetString("sw-economy-route-failed"), args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnRemoveRoute(Entity<SWMinistryWorkConsoleComponent> ent, ref SWEconomyRemoveRouteMessage args)
    {
        if (!_time.ModeActive || !_time.DayActive)
            return;

        if (!IsEconomyCard(ent))
            return;

        _economy.RemoveRoute(args.RouteId);
        UpdateUi(ent);
    }

    private void OnDeclareStimulus(Entity<SWMinistryWorkConsoleComponent> ent, ref SWEconomyDeclareStimulusMessage args)
    {
        if (!_time.ModeActive || !_time.DayActive)
            return;

        if (!IsEconomyCard(ent))
            return;

        _economy.DeclareStimulus();
        UpdateUi(ent);
    }

    private bool IsHealthCard(Entity<SWMinistryWorkConsoleComponent> ent) =>
        GetCardMinistry(ent) == SWMinistryHealthSystem.HealthMinistryId;

    private void OnHealthAction(Entity<SWMinistryWorkConsoleComponent> ent, ref SWHealthActionMessage args)
    {
        if (!IsHealthCard(ent) || !_health.HandleAction(args, args.Actor))
        {
            var reason = IsHealthCard(ent) && args.Action == SWHealthAction.OrderBatch
                ? _health.VaccineProductionUnavailableReason(args.WorldId) : null;
            _popup.PopupCursor(reason ?? "Действие недоступно: проверьте полномочия, этап работы, средства и снабжение.", args.Actor, PopupType.Medium);
        }
        UpdateUi(ent);
    }

    private void OnSetPosture(Entity<SWMinistryWorkConsoleComponent> ent, ref SWSecuritySetPostureMessage args)
    {
        if (!_time.ModeActive || !_time.DayActive)
            return;

        if (GetCardMinistry(ent) != SWMinistryInternalSecuritySystem.SecurityMinistryId)
            return;

        _security.SetPosture(args.Posture);
        UpdateUi(ent);
    }

    private void OnSecurityOrder(Entity<SWMinistryWorkConsoleComponent> ent, ref SWSecurityTeamOrderMessage args)
    {
        if (!_time.ModeActive || !_time.DayActive)
            return;

        if (GetCardMinistry(ent) != SWMinistryInternalSecuritySystem.SecurityMinistryId)
            return;

        var target = new SWStrategicMapDisclosure(_proto).ResolveSecurityTarget(
            _security.BuildMapState(), args.TeamId, args.Order, args.WorldId, args.CaseId);
        if (!_security.ExecuteOrder(args.TeamId, args.Order, target, args.CaseId))
            _popup.PopupCursor("Приказ не принят. Проверьте доступность группы, маршрут и текущий этап расследования.", args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnSecurityCell(Entity<SWMinistryWorkConsoleComponent> ent, ref SWSecurityCellCommandMessage args)
    {
        if (!_time.ModeActive || !_time.DayActive)
            return;

        if (GetCardMinistry(ent) != SWMinistryInternalSecuritySystem.SecurityMinistryId)
            return;

        if (!_security.SetCell(args.WorldId, args.Deploy))
            _popup.PopupCursor("Сеть не изменена. Проверьте доступность планеты, свободные ячейки и бюджет.", args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnWorldDefenseProgram(Entity<SWMinistryWorkConsoleComponent> ent, ref SWDefenseWorldProgramMessage args)
    {
        if (!_time.ModeActive || !_time.DayActive
            || GetCardMinistry(ent) != SWMinistryDefenseSystem.DefenseMinistryId)
            return;

        if (!_defense.StartWorldProgram(args.WorldId, args.ProgramId))
            _popup.PopupCursor("Проект не запущен: проверьте бюджет и существующие проекты округа.", args.Actor, PopupType.Medium);
        UpdateUi(ent);
    }

    private void OnWorldDefenseResupply(Entity<SWMinistryWorkConsoleComponent> ent, ref SWDefenseWorldResupplyMessage args)
    {
        if (!_time.ModeActive || !_time.DayActive
            || GetCardMinistry(ent) != SWMinistryDefenseSystem.DefenseMinistryId)
            return;

        if (!_defense.ResupplyWorld(args.WorldId))
            _popup.PopupCursor("Поставка не заказана: проверьте бюджет, наличие обороны и её резерв.", args.Actor, PopupType.Medium);
        UpdateUi(ent);
    }

    private void OnRecruitFleet(Entity<SWMinistryWorkConsoleComponent> ent, ref SWDefenseRecruitMessage args)
    {
        if (!_time.ModeActive || !_time.DayActive
            || GetCardMinistry(ent) != SWMinistryDefenseSystem.DefenseMinistryId)
            return;

        if (!_defense.RecruitFleet(args.WorldId))
            _popup.PopupCursor("Заказ отклонён: проверьте бюджет и доступность верфи.", args.Actor, PopupType.Medium);
        UpdateUi(ent);
    }

    private void OnFleetOrder(Entity<SWMinistryWorkConsoleComponent> ent, ref SWDefenseFleetOrderMessage args)
    {
        if (!_time.ModeActive || !_time.DayActive
            || GetCardMinistry(ent) != SWMinistryDefenseSystem.DefenseMinistryId)
            return;

        if (!_defense.IssueFleetOrder(args.FleetId, args.Order, args.Target, args.EventId))
            _popup.PopupCursor("Приказ отклонён: проверьте состояние соединения, маршрут и бюджет.", args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnLaunchOp(Entity<SWMinistryWorkConsoleComponent> ent, ref SWDefenseLaunchOpMessage args)
    {
        if (!_time.ModeActive || !_time.DayActive)
            return;

        if (GetCardMinistry(ent) != SWMinistryDefenseSystem.DefenseMinistryId)
            return;

        if (!_defense.LaunchOperation(args.OpId, args.Target))
            _popup.PopupCursor(Loc.GetString("sw-defense-op-failed"), args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnDeployFleet(Entity<SWMinistryWorkConsoleComponent> ent, ref SWFleetDeployMessage args)
    {
        if (!_time.ModeActive || !_time.DayActive)
            return;

        if (GetCardMinistry(ent) != SWMinistryDefenseSystem.DefenseMinistryId)
            return;

        _defense.DeployGroup(args.GroupId, args.Target);
        UpdateUi(ent);
    }

    private void OnResign(Entity<SWMinistryWorkConsoleComponent> ent, ref SWMinistryResignMessage args)
    {
        if (!_time.ModeActive || !_time.DayActive)
            return;

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
        if (!_time.ModeActive || !_time.DayActive)
            return;

        if (GetCardMinistry(ent) != SWMinistryDefenseSystem.DefenseMinistryId)
            return;

        _defense.SetStance(args.Stance);
        UpdateUi(ent);
    }

    private string? GetCardMinistry(Entity<SWMinistryWorkConsoleComponent> ent)
    {
        var card = _itemSlots.GetItemOrNull(ent.Owner, ent.Comp.IdSlot);
        if (card != null)
            _ministry.ReconcileCard(card.Value);
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
        if (!_time.ModeActive || !_time.DayActive)
            return;

        if (GetCardMinistry(ent) is not { } ministryId)
            return;

        if (!_dilemma.AuditRequest(ministryId, args.RequestId))
            _popup.PopupCursor(Loc.GetString("sw-ministry-audit-failed"), args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnResolve(Entity<SWMinistryWorkConsoleComponent> ent, ref SWMinistryResolveRequestMessage args)
    {
        if (!_time.ModeActive || !_time.DayActive)
            return;

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
        if (!_time.ModeActive || !_time.DayActive)
            return;

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
        if (!_time.ModeActive || !_time.DayActive)
            return;

        if (args.Container.ID == ent.Comp.IdSlot)
            UpdateUi(ent);
    }

    private void OnRemoved(Entity<SWMinistryWorkConsoleComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (!_time.ModeActive || !_time.DayActive)
            return;

        if (args.Container.ID == ent.Comp.IdSlot)
            UpdateUi(ent);
    }

    public override void Update(float frameTime)
    {
        if (!_time.ModeActive)
            return;

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
        if (card != null)
            _ministry.ReconcileCard(card.Value);

        var state = new SWMinistryWorkConsoleState
        {
            HasCard = card != null,
            GameTime = _time.TimeString(),
            Globals = _senate.BuildGlobalEntries(),
            SituationalStats = _senate.BuildSituationalStats(null),
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
                foreach (var extra in proto.ExtraDomains)
                    state.ExtraDomainStatIds.Add(extra);

                var ministryId = ministryComp.Ministry.Value.Id;
                var holderName = _ministry.GetHolder(ministryId);
                if (holderName != null)
                {
                    state.HasPersonal = true;
                    state.MyCorruption = _corruption.PersonCorruptionInt(holderName);
                    state.MyAccount = _corruption.Account(holderName);
                }
                state.MinistryBudget = _dilemma.GetMinistryBudget(ministryId);
                state.MinistryAdmin = _dilemma.GetMinistryAdmin(ministryId);
                state.AuditCost = _dilemma.AuditCost;
                state.Requests = _dilemma.BuildMinistryRequests(ministryId);
                state.Ambitions = _ambition.BuildAmbitions(ministryId);

                if (ministryId == SWMinistryDefenseSystem.DefenseMinistryId)
                {
                    state.IsDefense = true;
                    state.DefenseMap = new SWStrategicMapDisclosure(_proto).Defense(_defense.BuildMapState());
                    state.DefensePlanetary = _defense.BuildPlanetaryState();
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
                    state.DefenseLog = _defense.GetLog().Select(new SWStrategicMapDisclosure(_proto).Text).ToList();
                }
                else if (ministryId == SWMinistryInternalSecuritySystem.SecurityMinistryId)
                {
                    state.IsSecurity = true;
                    state.SecurityMap = new SWStrategicMapDisclosure(_proto).Security(_security.BuildMapState());
                    state.Posture = _security.Posture;
                    state.PostureName = SWMinistryInternalSecuritySystem.PostureName(_security.Posture);
                    state.PostureColor = SWMinistryInternalSecuritySystem.PostureColor(_security.Posture);
                    state.PostureEffects = _security.BuildPostureEffects(_security.Posture);
                    state.EmergencyPending = _security.EmergencyPending;
                    state.PendingPostureName = _security.PendingPosture is { } pend ? SWMinistryInternalSecuritySystem.PostureName(pend) : string.Empty;
                    state.Threat = _security.Threat;
                    state.ThreatStatus = _security.ThreatStatus();
                    state.Unrest = _security.Unrest;
                    state.UnrestStatus = _security.UnrestStatus();
                    state.SecurityLog = _security.GetLog().Select(new SWStrategicMapDisclosure(_proto).Text).ToList();
                }
                else if (ministryId == SWMinistryHealthSystem.HealthMinistryId)
                {
                    state.IsHealth = true;
                    state.Health = _health.BuildHealthView();
                    state.HealthEconomy = _healthEconomy.View;
                    state.HealthCrises = _health.BuildPublicMapCrises();
                    state.PathogenActive = _health.PathogenActive;
                    state.PathogenName = _health.PathogenName;
                    state.PathogenStrain = _health.PathogenStrain + 1;
                    state.Virulence = _health.Virulence;
                    state.PandemicStatus = _health.PandemicStatus();
                    state.PandemicColor = _health.PandemicColor();
                    state.InfectionAvg = (int) MathF.Round(_health.InfectionAvg());
                    state.Immunity = _health.Immunity;
                    state.Research = _health.Research;
                    state.VaccineReady = _health.VaccineReady;
                    state.VaccineStale = _health.VaccineStale;
                    state.ResearchFunding = _health.ResearchFunding;
                    state.ResearchFundingLabel = _health.ResearchFundingLabel();
                    state.HealthEmergencyActive = _health.EmergencyActive;
                    state.HealthEmergencyHoursLeft = _health.EmergencyHoursLeft;
                    state.HospitalServicePercent = _health.HospitalServicePercent;
                    state.HealthEmergencyPending = _health.EmergencyPending;
                    state.InfectionSectors = _health.BuildInfectionSectors();
                    state.HealthLog = _health.GetLog();
                    state.HospitalMaxLevel = SWMinistryHealthSystem.HospitalMaxLevel;
                }
                else if (ministryId == SWMinistryEconomySystem.EconomyMinistryId)
                {
                    state.IsEconomy = true;
                    state.EconSelectedGood = _economy.SelectedGood;
                    state.EconGoods = _economy.BuildIndustrialGoods();
                    state.EconProblems = _economy.BuildIndustrialProblems();
                    state.EconCandidates = _economy.BuildIndustrialCandidates();
                    state.EconIndustrialProjects = _economy.BuildIndustrialProjects();
                    state.EconRoutes = _economy.BuildIndustrialRoutes();
                    state.EconEvents = _economy.BuildEconomyEvents();
                    state.EconMapCrises = _mapStatistics.BuildMapCrises();
                    state.EconPhase = _economy.Phase;
                    state.EconPhaseName = SWMinistryEconomySystem.PhaseName(_economy.Phase);
                    state.EconPhaseColor = SWMinistryEconomySystem.PhaseColor(_economy.Phase);
                    state.Growth = _economy.Growth;
                    state.Overheat = _economy.Overheat;
                    state.OverheatStatus = _economy.OverheatStatus();
                    state.EconPolicy = _economy.Policy;
                    state.EconPolicyLabel = _economy.PolicyLabel();
                    state.StimulusPending = _economy.StimulusPending;
                    state.SelectedEconProgram = _economy.SelectedProgram ?? string.Empty;
                    state.EconProgramProgress = _economy.ProgramProgress;
                    state.Megaprojects = _economy.BuildProjects();
                    state.ActiveProjects = _economy.BuildActiveProjects();
                    state.ProjectSlots = _economy.ProjectSlots;
                    state.ProjectUsed = _economy.ProjectUsed;
                    state.TradeRoutes = _economy.BuildRoutes();
                    state.TradeRouteCap = _economy.RouteCap;
                    state.EconSectors = _economy.BuildSectors();
                    state.EconomyLog = _economy.GetLog();
                }
                else if (ministryId == SWMinistryFinanceSystem.FinanceMinistryId)
                {
                    state.IsFinance = true;
                    state.Treasury = _finance.TreasuryValue;
                    state.KeyRate = _finance.KeyRate;
                    state.RateOptions = new List<int>(_finance.RateOptions);
                    state.Inflation = _finance.Inflation;
                    state.InflationStatus = _finance.InflationStatus();
                    state.PricePercent = _finance.PricePercent();
                    state.Debt = _finance.Debt;
                    state.DebtStatus = _finance.DebtStatus();
                    state.CreditRating = _finance.CreditRating();
                    state.FinanceRatingReason = _finance.RatingReason;
                    state.FinanceCreditDebt = _finance.CreditDebt;
                    state.FinanceCoverageAvailable = _finance.HasTaxHistory && _finance.CreditDebt > 0;
                    state.FinanceDebtCoverage = (float) _finance.DebtCoverage;
                    state.RatingColor = _finance.RatingColor();
                    state.TaxLevel = _finance.TaxLevel;
                    state.TaxLevelLabel = _finance.TaxLevelLabel();
                    state.CanIssueBonds = _finance.CanIssueBonds();
                    state.BondProceeds = _finance.BondProceeds();
                    state.CanRepayDebt = _finance.CanRepayDebt();
                    state.NextRepaymentCost = _finance.NextRepaymentCost;
                    state.EmissionPending = _finance.EmissionPending;
                    state.FinanceLog = _finance.GetLog();

                    var phase = _budget.Phase;
                    state.BudgetPhaseLabel = phase switch
                    {
                        SWBudgetPhase.Submitted => "На одобрении Канцлера",
                        SWBudgetPhase.Approved => "Одобрен",
                        _ => _budget.Rejects > 0 ? "Отклонён - переделайте" : "Черновик",
                    };
                    state.BudgetPhaseColor = phase switch
                    {
                        SWBudgetPhase.Submitted => Color.FromHex("#E0A23B"),
                        SWBudgetPhase.Approved => Color.FromHex("#43D16A"),
                        _ => _budget.Rejects > 0 ? Color.FromHex("#E0556B") : Color.FromHex("#5DADE2"),
                    };
                    state.BudgetDayTotal = _budget.Total();
                    state.BudgetSectorPool = _budget.SectorPool();
                    state.BudgetRepublicPool = _budget.RepublicPool();
                    state.BudgetRepublicRemaining = _budget.Available(SWSenateDilemmaSystem.RepublicOwner);
                    state.BudgetRepublicPct = _budget.RepublicPct;
                    state.BudgetMinistryPool = _budget.MinistryPool();
                    state.BudgetSectorPct = _budget.SectorPct;
                    state.BudgetReservePct = _budget.ReservePct;
                    state.BudgetReservePool = _budget.ReservePool();
                    state.BudgetReserveRemaining = _budget.ReserveBalance;
                    state.BudgetTreasury = _budget.Treasury;
                    state.BudgetRevision = _budget.Revision;
                    state.BudgetDay = _time.Day;
                    state.BudgetAutoApplied = _budget.AutoApplied;
                    state.BudgetAutoReason = _budget.AutoReason;
                    state.BudgetCanTransfer = _budget.Approved;
                    state.BudgetTransferResultRequestId = _budget.TransferResultRequestId;
                    state.BudgetTransferResult = _budget.TransferResult;
                    state.BudgetTransferHistory = new(_budget.TransferHistory);
                    state.BudgetTransferTargets = _budget.BuildTransferTargets();
                    state.FinanceTaxGoods = _finance.TaxGoods();
                    state.FinanceReliefs = _finance.TaxReliefs();
                    state.FinanceTaxResultRequestId = _finance.TaxResultRequestId;
                    state.FinanceTaxResult = _finance.TaxResult;
                    state.FinanceGovernorRequests = _governor.FinanceRequests();
                    state.FinanceGovernorResult = _governor.FinanceResult;
                    state.FinanceGovernorResultRequestId = _governor.FinanceResultRequestId;
                    state.FinanceRevenueSectors = _finance.SectorRevenue();
                    state.BudgetRejects = _budget.Rejects;
                    state.BudgetRejectReason = _budget.RejectReason;
                    state.BudgetCanEdit = phase == SWBudgetPhase.Draft;
                    state.BudgetCanSubmit = phase == SWBudgetPhase.Draft;
                    state.BudgetShares = _budget.BuildShares();
                }
                else if (ministryId == SWMinistryJusticeSystem.JusticeMinistryId)
                {
                    state.IsJustice = true;
                    state.JusticeInspections = _justice.BuildInspectionState();
                    state.JusticeStance = _justice.Stance;
                    state.JusticeStanceName = SWMinistryJusticeSystem.StanceName(_justice.Stance);
                    state.JusticeStanceColor = SWMinistryJusticeSystem.StanceColor(_justice.Stance);
                    state.Backlog = _justice.Backlog;
                    state.BacklogStatus = _justice.BacklogStatus();
                    state.CourtCapacity = _justice.Capacity;
                    state.CourtCapacityMax = _justice.CapacityMax;
                    state.OvertimeCost = _justice.OvertimeCost();
                    state.CanBuyOvertime = _justice.CanBuyOvertime();
                    state.TribunalActive = _justice.TribunalActive;
                    state.TribunalHoursLeft = _justice.TribunalHoursLeft;
                    state.TribunalPending = _justice.TribunalPending;
                    state.CaseCount = _justice.CaseCount;
                    state.JusticeLog = _justice.GetLog();
                    state.JusticeArchive = _justice.BuildArchive();
                    state.JusticeResolved = _justice.Resolved;
                    state.JusticeConvictJust = _justice.ConvictJust;
                    state.JusticeConvictWrong = _justice.ConvictWrong;
                    state.JusticeAcquitJust = _justice.AcquitJust;
                    state.JusticeAcquitWrong = _justice.AcquitWrong;
                    state.JusticeDismissed = _justice.Dismissed;
                    state.JusticeOverdue = _justice.Overdue;
                    state.JusticeAccuracy = _justice.Accuracy();

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

            list.Add(new SWFleetSectorEntry { Id = sectorId, Name = proto.Name, Color = proto.Color, Defense = defense, Shipyard = proto.Shipyard });
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
