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
    [Dependency] private readonly SWMinistryInternalSecuritySystem _security = default!;
    [Dependency] private readonly SWMinistryHealthSystem _health = default!;
    [Dependency] private readonly SWMinistryEconomySystem _economy = default!;
    [Dependency] private readonly SWMinistryFinanceSystem _finance = default!;
    [Dependency] private readonly SWSenateBudgetSystem _budget = default!;
    [Dependency] private readonly SWMinistryJusticeSystem _justice = default!;
    [Dependency] private readonly SWCorruptionSystem _corruption = default!;
    [Dependency] private readonly SWMinistrySystem _ministry = default!;
    [Dependency] private readonly SWSenateAmbitionSystem _ambition = default!;

    private const int ScreenCost = 600;
    private const int ProsecuteCost = 1200;
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
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWSecuritySetPostureMessage>(OnSetPosture);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWSecuritySelectProgramMessage>(OnSelectSecProgram);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWSecurityDeployCellMessage>(OnDeployCell);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWSecurityLaunchOpMessage>(OnLaunchSecOp);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWSecurityRaidPlotMessage>(OnRaidPlot);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWHealthSetFundingMessage>(OnSetFunding);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWHealthDeployVaccineMessage>(OnDeployVaccine);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWHealthQuarantineMessage>(OnQuarantine);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWHealthBuildHospitalMessage>(OnBuildHospital);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWHealthEmergencyAidMessage>(OnEmergencyAid);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWHealthDeclareEmergencyMessage>(OnDeclareEmergency);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWHealthSelectProgramMessage>(OnSelectHealthProgram);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWEconomySetPolicyMessage>(OnSetEconPolicy);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWEconomyLaunchProjectMessage>(OnLaunchProject);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWEconomyCancelProjectMessage>(OnCancelProject);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWEconomyAddRouteMessage>(OnAddRoute);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWEconomyRemoveRouteMessage>(OnRemoveRoute);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWEconomyDeclareStimulusMessage>(OnDeclareStimulus);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWEconomySelectProgramMessage>(OnSelectEconProgram);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWFinanceSetRateMessage>(OnSetRate);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWFinanceSetTaxMessage>(OnSetTax);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWFinanceIssueBondsMessage>(OnIssueBonds);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWFinanceRepayDebtMessage>(OnRepayDebt);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWFinanceEmissionMessage>(OnEmission);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWFinanceSelectProgramMessage>(OnSelectFinProgram);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWBudgetAdjustRatioMessage>(OnBudgetRatio);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWBudgetSetWeightMessage>(OnBudgetWeight);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWBudgetResetWeightsMessage>(OnBudgetResetWeights);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWBudgetSubmitMessage>(OnBudgetSubmit);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWJusticeInvestigateMessage>(OnInvestigate);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWJusticeSendToCourtMessage>(OnSendToCourt);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWJusticeHoldHearingMessage>(OnHoldHearing);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWJusticeVerdictMessage>(OnVerdict);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWJusticeSetStanceMessage>(OnSetJusticeStance);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWJusticeSelectProgramMessage>(OnSelectJusticeProgram);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWJusticeDeclareTribunalMessage>(OnDeclareTribunal);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWJusticeOvertimeMessage>(OnOvertime);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWCorruptionScreenMessage>(OnScreen);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWCorruptionProsecuteMessage>(OnProsecute);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, SWMinistryResignMessage>(OnResign);
    }

    private void OnScreen(Entity<SWMinistryWorkConsoleComponent> ent, ref SWCorruptionScreenMessage args)
    {
        if (!IsJusticeCard(ent))
            return;

        if (_dilemma.SpendMinistry(SWMinistryJusticeSystem.JusticeMinistryId, ScreenCost))
            _corruption.Screen(args.Sector, args.Key);
        else
            _popup.PopupCursor(Loc.GetString("sw-corruption-failed"), args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnProsecute(Entity<SWMinistryWorkConsoleComponent> ent, ref SWCorruptionProsecuteMessage args)
    {
        if (!IsJusticeCard(ent))
            return;

        if (_dilemma.SpendMinistry(SWMinistryJusticeSystem.JusticeMinistryId, ProsecuteCost))
            _popup.PopupCursor(_corruption.Prosecute(args.Sector, args.Key), args.Actor, PopupType.Medium);
        else
            _popup.PopupCursor(Loc.GetString("sw-corruption-failed"), args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnOvertime(Entity<SWMinistryWorkConsoleComponent> ent, ref SWJusticeOvertimeMessage args)
    {
        if (!IsJusticeCard(ent))
            return;

        if (!_justice.BuyOvertime())
            _popup.PopupCursor(Loc.GetString("sw-justice-action-failed"), args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private bool IsJusticeCard(Entity<SWMinistryWorkConsoleComponent> ent) =>
        GetCardMinistry(ent) == SWMinistryJusticeSystem.JusticeMinistryId;

    private void OnInvestigate(Entity<SWMinistryWorkConsoleComponent> ent, ref SWJusticeInvestigateMessage args)
    {
        if (!IsJusticeCard(ent))
            return;

        if (!_justice.Investigate(args.CaseId))
            _popup.PopupCursor(Loc.GetString("sw-justice-action-failed"), args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnSendToCourt(Entity<SWMinistryWorkConsoleComponent> ent, ref SWJusticeSendToCourtMessage args)
    {
        if (!IsJusticeCard(ent))
            return;

        _justice.SendToCourt(args.CaseId);
        UpdateUi(ent);
    }

    private void OnHoldHearing(Entity<SWMinistryWorkConsoleComponent> ent, ref SWJusticeHoldHearingMessage args)
    {
        if (!IsJusticeCard(ent))
            return;

        if (!_justice.HoldHearing(args.CaseId))
            _popup.PopupCursor(Loc.GetString("sw-justice-action-failed"), args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnVerdict(Entity<SWMinistryWorkConsoleComponent> ent, ref SWJusticeVerdictMessage args)
    {
        if (!IsJusticeCard(ent))
            return;

        _justice.Rule(args.CaseId, args.Verdict);
        UpdateUi(ent);
    }

    private void OnSetJusticeStance(Entity<SWMinistryWorkConsoleComponent> ent, ref SWJusticeSetStanceMessage args)
    {
        if (!IsJusticeCard(ent))
            return;

        _justice.SetStance(args.Stance);
        UpdateUi(ent);
    }

    private void OnSelectJusticeProgram(Entity<SWMinistryWorkConsoleComponent> ent, ref SWJusticeSelectProgramMessage args)
    {
        if (!IsJusticeCard(ent))
            return;

        _justice.SelectProgram(args.ProgramId);
        UpdateUi(ent);
    }

    private void OnDeclareTribunal(Entity<SWMinistryWorkConsoleComponent> ent, ref SWJusticeDeclareTribunalMessage args)
    {
        if (!IsJusticeCard(ent))
            return;

        _justice.DeclareTribunal();
        UpdateUi(ent);
    }

    private void OnBudgetRatio(Entity<SWMinistryWorkConsoleComponent> ent, ref SWBudgetAdjustRatioMessage args)
    {
        if (!IsFinanceCard(ent))
            return;

        _budget.AdjustRatio(args.DeltaPct);
        UpdateUi(ent);
    }

    private void OnBudgetWeight(Entity<SWMinistryWorkConsoleComponent> ent, ref SWBudgetSetWeightMessage args)
    {
        if (!IsFinanceCard(ent))
            return;

        _budget.SetWeight(args.MinistryId, args.Delta);
        UpdateUi(ent);
    }

    private void OnBudgetResetWeights(Entity<SWMinistryWorkConsoleComponent> ent, ref SWBudgetResetWeightsMessage args)
    {
        if (!IsFinanceCard(ent))
            return;

        _budget.ResetWeights();
        UpdateUi(ent);
    }

    private void OnBudgetSubmit(Entity<SWMinistryWorkConsoleComponent> ent, ref SWBudgetSubmitMessage args)
    {
        if (!IsFinanceCard(ent))
            return;

        if (_budget.Submit())
            _popup.PopupCursor(Loc.GetString("sw-budget-submitted"), args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private bool IsFinanceCard(Entity<SWMinistryWorkConsoleComponent> ent) =>
        GetCardMinistry(ent) == SWMinistryFinanceSystem.FinanceMinistryId;

    private void OnSetRate(Entity<SWMinistryWorkConsoleComponent> ent, ref SWFinanceSetRateMessage args)
    {
        if (!IsFinanceCard(ent))
            return;

        _finance.SetRate(args.Rate);
        UpdateUi(ent);
    }

    private void OnSetTax(Entity<SWMinistryWorkConsoleComponent> ent, ref SWFinanceSetTaxMessage args)
    {
        if (!IsFinanceCard(ent))
            return;

        _finance.SetTax(args.Level);
        UpdateUi(ent);
    }

    private void OnIssueBonds(Entity<SWMinistryWorkConsoleComponent> ent, ref SWFinanceIssueBondsMessage args)
    {
        if (!IsFinanceCard(ent))
            return;

        if (!_finance.IssueBonds())
            _popup.PopupCursor(Loc.GetString("sw-finance-bonds-failed"), args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnRepayDebt(Entity<SWMinistryWorkConsoleComponent> ent, ref SWFinanceRepayDebtMessage args)
    {
        if (!IsFinanceCard(ent))
            return;

        if (!_finance.RepayDebt())
            _popup.PopupCursor(Loc.GetString("sw-finance-repay-failed"), args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnEmission(Entity<SWMinistryWorkConsoleComponent> ent, ref SWFinanceEmissionMessage args)
    {
        if (!IsFinanceCard(ent))
            return;

        _finance.DeclareEmission();
        UpdateUi(ent);
    }

    private void OnSelectFinProgram(Entity<SWMinistryWorkConsoleComponent> ent, ref SWFinanceSelectProgramMessage args)
    {
        if (!IsFinanceCard(ent))
            return;

        _finance.SelectProgram(args.ProgramId);
        UpdateUi(ent);
    }

    private bool IsEconomyCard(Entity<SWMinistryWorkConsoleComponent> ent) =>
        GetCardMinistry(ent) == SWMinistryEconomySystem.EconomyMinistryId;

    private void OnSetEconPolicy(Entity<SWMinistryWorkConsoleComponent> ent, ref SWEconomySetPolicyMessage args)
    {
        if (!IsEconomyCard(ent))
            return;

        _economy.SetPolicy(args.Level);
        UpdateUi(ent);
    }

    private void OnLaunchProject(Entity<SWMinistryWorkConsoleComponent> ent, ref SWEconomyLaunchProjectMessage args)
    {
        if (!IsEconomyCard(ent))
            return;

        if (!_economy.LaunchProject(args.ProjectId, args.Target))
            _popup.PopupCursor(Loc.GetString("sw-economy-project-failed"), args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnCancelProject(Entity<SWMinistryWorkConsoleComponent> ent, ref SWEconomyCancelProjectMessage args)
    {
        if (!IsEconomyCard(ent))
            return;

        _economy.CancelProject(args.Index);
        UpdateUi(ent);
    }

    private void OnAddRoute(Entity<SWMinistryWorkConsoleComponent> ent, ref SWEconomyAddRouteMessage args)
    {
        if (!IsEconomyCard(ent))
            return;

        if (!_economy.AddRoute(args.AId, args.BId))
            _popup.PopupCursor(Loc.GetString("sw-economy-route-failed"), args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnRemoveRoute(Entity<SWMinistryWorkConsoleComponent> ent, ref SWEconomyRemoveRouteMessage args)
    {
        if (!IsEconomyCard(ent))
            return;

        _economy.RemoveRoute(args.RouteId);
        UpdateUi(ent);
    }

    private void OnDeclareStimulus(Entity<SWMinistryWorkConsoleComponent> ent, ref SWEconomyDeclareStimulusMessage args)
    {
        if (!IsEconomyCard(ent))
            return;

        _economy.DeclareStimulus();
        UpdateUi(ent);
    }

    private void OnSelectEconProgram(Entity<SWMinistryWorkConsoleComponent> ent, ref SWEconomySelectProgramMessage args)
    {
        if (!IsEconomyCard(ent))
            return;

        _economy.SelectProgram(args.ProgramId);
        UpdateUi(ent);
    }

    private bool IsHealthCard(Entity<SWMinistryWorkConsoleComponent> ent) =>
        GetCardMinistry(ent) == SWMinistryHealthSystem.HealthMinistryId;

    private void OnSetFunding(Entity<SWMinistryWorkConsoleComponent> ent, ref SWHealthSetFundingMessage args)
    {
        if (!IsHealthCard(ent))
            return;

        _health.SetFunding(args.Level);
        UpdateUi(ent);
    }

    private void OnDeployVaccine(Entity<SWMinistryWorkConsoleComponent> ent, ref SWHealthDeployVaccineMessage args)
    {
        if (!IsHealthCard(ent))
            return;

        if (!_health.DeployVaccine())
            _popup.PopupCursor(Loc.GetString("sw-health-vaccine-failed"), args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnQuarantine(Entity<SWMinistryWorkConsoleComponent> ent, ref SWHealthQuarantineMessage args)
    {
        if (!IsHealthCard(ent))
            return;

        _health.SetQuarantine(args.SectorId, args.On);
        UpdateUi(ent);
    }

    private void OnBuildHospital(Entity<SWMinistryWorkConsoleComponent> ent, ref SWHealthBuildHospitalMessage args)
    {
        if (!IsHealthCard(ent))
            return;

        if (!_health.BuildHospital(args.SectorId))
            _popup.PopupCursor(Loc.GetString("sw-health-build-failed"), args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnEmergencyAid(Entity<SWMinistryWorkConsoleComponent> ent, ref SWHealthEmergencyAidMessage args)
    {
        if (!IsHealthCard(ent))
            return;

        if (!_health.EmergencyAid(args.SectorId))
            _popup.PopupCursor(Loc.GetString("sw-health-aid-failed"), args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnDeclareEmergency(Entity<SWMinistryWorkConsoleComponent> ent, ref SWHealthDeclareEmergencyMessage args)
    {
        if (!IsHealthCard(ent))
            return;

        _health.DeclareEmergency();
        UpdateUi(ent);
    }

    private void OnSelectHealthProgram(Entity<SWMinistryWorkConsoleComponent> ent, ref SWHealthSelectProgramMessage args)
    {
        if (!IsHealthCard(ent))
            return;

        _health.SelectProgram(args.ProgramId);
        UpdateUi(ent);
    }

    private void OnSetPosture(Entity<SWMinistryWorkConsoleComponent> ent, ref SWSecuritySetPostureMessage args)
    {
        if (GetCardMinistry(ent) != SWMinistryInternalSecuritySystem.SecurityMinistryId)
            return;

        _security.SetPosture(args.Posture);
        UpdateUi(ent);
    }

    private void OnSelectSecProgram(Entity<SWMinistryWorkConsoleComponent> ent, ref SWSecuritySelectProgramMessage args)
    {
        if (GetCardMinistry(ent) != SWMinistryInternalSecuritySystem.SecurityMinistryId)
            return;

        _security.SelectProgram(args.ProgramId);
        UpdateUi(ent);
    }

    private void OnDeployCell(Entity<SWMinistryWorkConsoleComponent> ent, ref SWSecurityDeployCellMessage args)
    {
        if (GetCardMinistry(ent) != SWMinistryInternalSecuritySystem.SecurityMinistryId)
            return;

        _security.DeployCell(args.CellId, args.Target);
        UpdateUi(ent);
    }

    private void OnLaunchSecOp(Entity<SWMinistryWorkConsoleComponent> ent, ref SWSecurityLaunchOpMessage args)
    {
        if (GetCardMinistry(ent) != SWMinistryInternalSecuritySystem.SecurityMinistryId)
            return;

        if (!_security.LaunchOperation(args.OpId, args.Target))
            _popup.PopupCursor(Loc.GetString("sw-defense-op-failed"), args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnRaidPlot(Entity<SWMinistryWorkConsoleComponent> ent, ref SWSecurityRaidPlotMessage args)
    {
        if (GetCardMinistry(ent) != SWMinistryInternalSecuritySystem.SecurityMinistryId)
            return;

        if (!_security.RaidPlot(args.PlotId))
            _popup.PopupCursor(Loc.GetString("sw-security-raid-failed"), args.Actor, PopupType.Medium);

        UpdateUi(ent);
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
                    state.DefenseLog = _defense.GetLog();
                }
                else if (ministryId == SWMinistryInternalSecuritySystem.SecurityMinistryId)
                {
                    state.IsSecurity = true;
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
                    state.SelectedSecProgram = _security.SelectedProgram ?? string.Empty;
                    state.SecProgramProgress = _security.ProgramProgress;
                    state.SecPrograms = _security.BuildPrograms();
                    state.Cells = _security.BuildCells();
                    state.CellSectors = _security.BuildCellSectors();
                    state.CellUpkeep = _security.CellUpkeep;
                    state.SecOperations = _security.BuildOperations();
                    state.SecActiveOps = _security.BuildActiveOps();
                    state.SecOpSlots = _security.ActiveOpSlots;
                    state.SecOpUsed = _security.ActiveOpUsed;
                    state.SecurityLog = _security.GetLog();
                    state.Plots = _security.BuildPlots();
                }
                else if (ministryId == SWMinistryHealthSystem.HealthMinistryId)
                {
                    state.IsHealth = true;
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
                    state.HealthEmergencyPending = _health.EmergencyPending;
                    state.SelectedHealthProgram = _health.SelectedProgram ?? string.Empty;
                    state.HealthProgramProgress = _health.ProgramProgress;
                    state.HealthPrograms = _health.BuildPrograms();
                    state.InfectionSectors = _health.BuildInfectionSectors();
                    state.HealthLog = _health.GetLog();
                    state.HospitalMaxLevel = SWMinistryHealthSystem.HospitalMaxLevel;
                }
                else if (ministryId == SWMinistryEconomySystem.EconomyMinistryId)
                {
                    state.IsEconomy = true;
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
                    state.EconPrograms = _economy.BuildPrograms();
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
                    state.RatingColor = _finance.RatingColor();
                    state.TaxLevel = _finance.TaxLevel;
                    state.TaxLevelLabel = _finance.TaxLevelLabel();
                    state.CanIssueBonds = _finance.CanIssueBonds();
                    state.BondProceeds = _finance.BondProceeds();
                    state.CanRepayDebt = _finance.CanRepayDebt();
                    state.EmissionPending = _finance.EmissionPending;
                    state.SelectedFinanceProgram = _finance.SelectedProgram ?? string.Empty;
                    state.FinanceProgramProgress = _finance.ProgramProgress;
                    state.FinancePrograms = _finance.BuildPrograms();
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
                    state.BudgetMinistryPool = _budget.MinistryPool();
                    state.BudgetSectorPct = _budget.SectorPct;
                    state.BudgetRejects = _budget.Rejects;
                    state.BudgetRejectReason = _budget.RejectReason;
                    state.BudgetCanEdit = phase != SWBudgetPhase.Approved;
                    state.BudgetCanSubmit = phase == SWBudgetPhase.Draft;
                    state.BudgetShares = _budget.BuildShares();
                }
                else if (ministryId == SWMinistryJusticeSystem.JusticeMinistryId)
                {
                    state.IsJustice = true;
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
                    state.SelectedJusticeProgram = _justice.SelectedProgram ?? string.Empty;
                    state.JusticeProgramProgress = _justice.ProgramProgress;
                    state.JusticePrograms = _justice.BuildPrograms();
                    state.Cases = _justice.BuildCases();
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

                    var jbudget = _dilemma.GetMinistryBudget(ministryId);
                    state.CorruptionIndex = _corruption.GlobalIndex();
                    state.CorruptionStatus = _corruption.GlobalStatus();
                    state.ScreenCost = ScreenCost;
                    state.ProsecuteCost = ProsecuteCost;
                    state.CorruptSectors = BuildCorruptSectors(jbudget);
                    state.CorruptPeople = BuildCorruptPeople(jbudget);
                    state.CorruptionLog = _corruption.GetLog();
                }
            }
        }

        _ui.SetUiState(ent.Owner, SWMinistryWorkConsoleUiKey.Key, state);
    }

    private List<SWCorruptTargetEntry> BuildCorruptSectors(int budget)
    {
        var list = new List<SWCorruptTargetEntry>();
        foreach (var (sectorId, holder) in _senate.SectorHolders)
        {
            if (!_proto.TryIndex<SWSectorPrototype>(sectorId, out var proto))
                continue;

            var revealed = _corruption.IsRevealed(true, sectorId);
            list.Add(new SWCorruptTargetEntry
            {
                Key = sectorId,
                Sector = true,
                Name = proto.Name,
                Role = $"Сенатор: {holder}",
                Color = proto.Color,
                Revealed = revealed,
                Corruption = revealed ? _corruption.SectorCorruptionInt(sectorId) : -1,
                Account = -1,
                CanScreen = !revealed && budget >= ScreenCost,
                CanProsecute = budget >= ProsecuteCost,
            });
        }

        list.Sort((a, b) => b.Corruption.CompareTo(a.Corruption));
        return list;
    }

    private List<SWCorruptTargetEntry> BuildCorruptPeople(int budget)
    {
        var roles = new Dictionary<string, string>();

        foreach (var (sectorId, holder) in _senate.SectorHolders)
            roles[holder] = $"Сенатор {(_proto.TryIndex<SWSectorPrototype>(sectorId, out var sp) ? sp.Name : sectorId)}";
        foreach (var (mid, holder) in _ministry.Holders)
            roles[holder] = _proto.TryIndex<SWMinistryPrototype>(mid, out var mp) ? $"Министр {mp.TitleName}" : "Министр";
        if (_senate.Chancellor != null)
            roles[_senate.Chancellor] = "Канцлер";

        var list = new List<SWCorruptTargetEntry>();
        foreach (var (name, role) in roles)
        {
            var revealed = _corruption.IsRevealed(false, name);
            list.Add(new SWCorruptTargetEntry
            {
                Key = name,
                Sector = false,
                Name = name,
                Role = role,
                Color = Color.FromHex("#C0A062"),
                Revealed = revealed,
                Corruption = revealed ? _corruption.PersonCorruptionInt(name) : -1,
                Account = revealed ? _corruption.Account(name) : -1,
                CanScreen = !revealed && budget >= ScreenCost,
                CanProsecute = budget >= ProsecuteCost,
            });
        }

        list.Sort((a, b) => b.Corruption.CompareTo(a.Corruption));
        return list;
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
