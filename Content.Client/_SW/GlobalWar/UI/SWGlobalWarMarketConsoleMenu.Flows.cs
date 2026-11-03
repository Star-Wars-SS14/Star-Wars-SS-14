using System.Globalization;
using System.Linq;
using Content.Shared._SW.GlobalWar;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client._SW.GlobalWar.UI;

public sealed partial class SWGlobalWarMarketConsoleMenu
{
    public SWGlobalWarFlowDraft FlowDraft = new();
    private bool _restoreFlowDraft;
    private bool _flowRestoring;
    private string? _editingFlow;
    private long _editingRevision = -1;
    private string[] _flowIds = [];
    private string[] _flowPlanets = [];
    private string[] _flowCommodities = [];
    private Guid[] _flowGrants = [];
    private SWGlobalWarFlowWorkerView[] _flowWorkers = [];
    private SWGlobalWarFlowCommand? _pendingFlow;
    private SWGlobalWarFlowGrantCommand? _pendingGrant;
    private SWGlobalWarFlowRevokeCommand? _pendingRevoke;
    private bool FlowPending => _pendingFlow != null || _pendingGrant != null || _pendingRevoke != null;
    public event Action<SWGlobalWarFlowCommand>? FlowRequested;
    public event Action<SWGlobalWarFlowGrantCommand>? FlowGrantRequested;
    public event Action<SWGlobalWarFlowRevokeCommand>? FlowRevokeRequested;

    private void InitializeFlowEditor()
    {
        FlowPicker.AddItem(Loc.GetString("sw-global-war-economy-select-flow"), -1);
        FlowPicker.SelectId(-1);
        FlowWorker.AddItem(Loc.GetString("sw-global-war-economy-select-worker"), -1);
        FlowWorker.SelectId(-1);
        FlowGrantPicker.AddItem(Loc.GetString("sw-global-war-economy-no-grant"), -1);
        FlowGrantPicker.SelectId(-1);
        FlowsTab.OnPressed += _ =>
        {
            GoodsScroll.Visible = FreightScroll.Visible = false;
            FlowsScroll.Visible = true;
            GrowthScroll.Visible = false;
            GoodsTab.SetAppearance(SWGlobalWarConsoleStyle.TextBright, selected: false, tab: true);
            FreightTab.SetAppearance(SWGlobalWarConsoleStyle.TextBright, selected: false, tab: true);
            FlowsTab.SetAppearance(SWGlobalWarConsoleStyle.TextBright, selected: true, tab: true);
            GrowthTab.SetAppearance(SWGlobalWarConsoleStyle.TextBright, selected: false, tab: true);
        };
        foreach (var picker in new[] { FlowSource, FlowDestination, FlowCommodity, FlowWorker, FlowGrantPicker })
            picker.OnItemSelected += args =>
            {
                picker.SelectId(args.Id);
                CaptureFlowDraft();
                RefreshFlowContext();
            };
        foreach (var input in new[] { FlowRate, FlowOrder, GrantMinimum, GrantMaximum, GrantMinutes })
            input.OnTextChanged += _ => { CaptureFlowDraft(); RefreshFlowContext(); };
        FlowEnabled.OnToggled += _ => { CaptureFlowDraft(); RefreshFlowContext(); };
        FlowHeadDecision.OnToggled += _ => CaptureFlowDraft();
        LoadCurrentFlow.OnPressed += _ =>
        {
            if (!FlowPending && _state?.Flows.FirstOrDefault(f => f.Id == _editingFlow) is { } current)
                LoadFlow(current);
        };
        ReapplyFlow.OnPressed += _ =>
        {
            if (FlowPending || _state?.Flows.FirstOrDefault(f => f.Id == _editingFlow) is not { } current)
                return;
            _editingRevision = current.Revision;
            SubmitFlow();
        };
        FlowPicker.OnItemSelected += args =>
        {
            if (_state == null || args.Id < 0 || args.Id >= _flowIds.Length || FlowPending)
                return;
            FlowPicker.SelectId(args.Id);
            var flow = _state.Flows.First(f => f.Id == _flowIds[args.Id]);
            if (flow.Id == _editingFlow)
                return;
            LoadFlow(flow);
        };
        NewFlow.OnPressed += _ =>
        {
            if (FlowPending)
                return;
            _editingFlow = null;
            _editingRevision = -1;
            FlowPicker.SelectId(-1);
            SetText(FlowResult, string.Empty);
            SetText(FlowCurrent, Loc.GetString("sw-global-war-economy-flow-new"));
            CaptureFlowDraft();
            RefreshFlowContext();
        };
        SaveFlow.OnPressed += _ => SubmitFlow();
        RetryFlow.OnPressed += _ =>
        {
            if (_pendingFlow != null)
                FlowRequested?.Invoke(_pendingFlow);
            if (_pendingGrant != null)
                FlowGrantRequested?.Invoke(_pendingGrant);
            if (_pendingRevoke != null)
                FlowRevokeRequested?.Invoke(_pendingRevoke);
        };
        GrantFlow.OnPressed += _ =>
        {
            if (_state == null || FlowPending || !ReadFlowSettings(out var settings))
                return;
            int minimum = 0, maximum = 0, minutes = 0;
            string? invalid = null;
            if (FlowWorker.SelectedId < 0 || FlowWorker.SelectedId >= _flowWorkers.Length)
                invalid = "sw-global-war-economy-select-worker";
            else if (!ReadRate(GrantMinimum.Text, out minimum) || !ReadRate(GrantMaximum.Text, out maximum))
                invalid = "sw-global-war-economy-invalid-rate";
            else if (maximum < minimum)
                invalid = "sw-global-war-economy-failure-invalidrange";
            else if (!int.TryParse(GrantMinutes.Text, out minutes) || minutes <= 0)
                invalid = "sw-global-war-economy-failure-invalidduration";
            if (invalid != null)
            {
                SetText(FlowResult, Loc.GetString(invalid));
                return;
            }
            var worker = _flowWorkers[FlowWorker.SelectedId];
            _pendingGrant = new(_state.Generation, Guid.NewGuid(), _state.AssignmentRevision, worker.User,
                worker.Revision, _editingFlow, settings.Source.Id, settings.Destination.Id, settings.Commodity.Id,
                minimum, maximum, settings.Order, settings.Order, TimeSpan.FromMinutes(minutes), FlowHeadDecision.Pressed);
            SetFlowPending();
            FlowGrantRequested?.Invoke(_pendingGrant);
        };
        RevokeFlow.OnPressed += _ =>
        {
            if (_state != null && !FlowPending && FlowGrantPicker.SelectedId >= 0 && FlowGrantPicker.SelectedId < _flowGrants.Length)
            {
                _pendingRevoke = new(_state.Generation, Guid.NewGuid(), _state.AssignmentRevision,
                    _flowGrants[FlowGrantPicker.SelectedId], FlowHeadDecision.Pressed);
                SetFlowPending();
                FlowRevokeRequested?.Invoke(_pendingRevoke);
            }
        };
    }

    private void SubmitFlow()
    {
        if (_state == null || FlowPending || !ReadFlowSettings(out var settings))
            return;
        var grant = FlowGrantPicker.SelectedId >= 0 && FlowGrantPicker.SelectedId < _flowGrants.Length
            ? _flowGrants[FlowGrantPicker.SelectedId] : Guid.Empty;
        _pendingFlow = new(_state.Generation, Guid.NewGuid(), _state.AssignmentRevision, _editingFlow,
            _editingRevision, settings, grant, FlowHeadDecision.Pressed);
        SetFlowPending();
        FlowRequested?.Invoke(_pendingFlow);
    }

    private void SetFlowPending()
    {
        SaveFlow.Disabled = GrantFlow.Disabled = RevokeFlow.Disabled = NewFlow.Disabled = FlowPicker.Disabled = true;
        RetryFlow.Disabled = false;
        SetText(FlowResult, Loc.GetString("sw-global-war-economy-pending"));
        CaptureFlowDraft();
    }

    private void ResetFlowEditor()
    {
        _flowRestoring = true;
        _pendingFlow = null;
        _pendingGrant = null;
        _pendingRevoke = null;
        FlowDraft.Clear();
        _editingFlow = null;
        _editingRevision = -1;
        _flowIds = _flowPlanets = [];
        _flowGrants = [];
        _flowWorkers = [];
        FlowPicker.Clear();
        FlowPicker.AddItem(Loc.GetString("sw-global-war-economy-select-flow"), -1);
        FlowPicker.SelectId(-1);
        FlowSource.Clear();
        FlowDestination.Clear();
        FlowGrantPicker.Clear();
        FlowGrantPicker.AddItem(Loc.GetString("sw-global-war-economy-no-grant"), -1);
        FlowGrantPicker.SelectId(-1);
        FlowWorker.Clear();
        FlowWorker.AddItem(Loc.GetString("sw-global-war-economy-select-worker"), -1);
        FlowWorker.SelectId(-1);
        FlowRate.Text = "0.1";
        FlowOrder.Text = "0";
        FlowHeadDecision.Pressed = false;
        SetText(FlowResult, string.Empty);
        SetText(FlowCurrent, string.Empty);
        SetText(FlowHistory, string.Empty);
        _flowRestoring = false;
    }

    private void LoadFlow(SWGlobalWarFlowView flow)
    {
        _flowRestoring = true;
        _editingFlow = flow.Id;
        _editingRevision = flow.Revision;
        FlowSource.SelectId(Array.IndexOf(_flowPlanets, flow.Settings.Source.Id));
        FlowDestination.SelectId(Array.IndexOf(_flowPlanets, flow.Settings.Destination.Id));
        FlowCommodity.SelectId(Array.IndexOf(_flowCommodities, flow.Settings.Commodity.Id));
        FlowRate.Text = RateText(flow.Settings.Rate);
        FlowOrder.Text = flow.Settings.Order.ToString(CultureInfo.InvariantCulture);
        FlowEnabled.Pressed = flow.Settings.Enabled;
        SetFlowSummary(flow);
        SetText(FlowResult, string.Empty);
        _flowRestoring = false;
        CaptureFlowDraft();
        RefreshFlowContext();
    }

    private void SetFlowSummary(SWGlobalWarFlowView flow)
    {
        SetText(FlowCurrent, Loc.GetString("sw-global-war-economy-current", ("rate", flow.PlannedRate.ToString("0.###")),
            ("revision", flow.Revision), ("reason", string.IsNullOrEmpty(flow.WaitReason) ?
                Loc.GetString("sw-global-war-economy-no-wait") : Loc.GetString(flow.WaitReason)),
            ("offer", flow.NeutralOffer is { } offer ? Loc.GetString("sw-global-war-economy-offer",
                ("rate", offer.ToString("0.###"))) : string.Empty)));
    }

    private void RefreshFlowEditor()
    {
        if (_state == null)
            return;
        _flowRestoring = true;
        FlowHeadDecision.Visible = _state.Head;
        var selectionLost = false;
        FlowDelegation.Visible = _state.CanManageFlows;
        FlowsTab.Disabled = _state.Generation == Guid.Empty || _state.Assignment == Guid.Empty;
        SaveFlow.Disabled = FlowPending ||
            !_state.CanManageFlows && !_state.FlowGrants.Any(g => !g.Revoked && g.Expires > _state.Time);
        GrantFlow.Disabled = !_state.CanManageFlows || FlowPending;
        NewFlow.Disabled = SaveFlow.Disabled;
        FlowPicker.Disabled = FlowPending;
        RetryFlow.Disabled = !FlowPending;
        var ids = _state.Flows.OrderByDescending(f => f.Settings.Destination.Id == _suggestedDestination &&
            f.Settings.Commodity.Id == _suggestedCommodity).Select(f => f.Id).ToArray();
        if (!_flowIds.SequenceEqual(ids))
        {
            _flowIds = ids;
            FlowPicker.Clear();
            FlowPicker.AddItem(Loc.GetString("sw-global-war-economy-select-flow"), -1);
            for (var i = 0; i < ids.Length; i++)
            {
                var flow = _state.Flows.Single(f => f.Id == ids[i]);
                FlowPicker.AddItem(Loc.GetString("sw-global-war-economy-flow-label",
                    ("source", PlanetName(flow.Settings.Source)), ("destination", PlanetName(flow.Settings.Destination)),
                    ("commodity", Loc.GetString(_prototypes.Index(flow.Settings.Commodity).Name))), i);
            }
            if (_editingFlow != null && ids.Contains(_editingFlow))
                FlowPicker.SelectId(Array.IndexOf(ids, _editingFlow));
            else
                FlowPicker.SelectId(-1);
        }
        for (var i = 0; i < _flowIds.Length; i++)
        {
            var flow = _state.Flows.Single(f => f.Id == _flowIds[i]);
            FlowPicker.SetItemText(i + 1, Loc.GetString("sw-global-war-economy-flow-label",
                ("source", PlanetName(flow.Settings.Source)), ("destination", PlanetName(flow.Settings.Destination)),
                ("commodity", Loc.GetString(_prototypes.Index(flow.Settings.Commodity).Name))));
        }
        if (_state.Flows.FirstOrDefault(f => f.Id == _editingFlow) is { } currentFlow)
            SetFlowSummary(currentFlow);
        var planets = _state.Planets.Select(p => p.Planet.Id).ToArray();
        if (!_flowPlanets.SequenceEqual(planets))
        {
            _flowPlanets = planets;
            FlowSource.Clear();
            FlowDestination.Clear();
            for (var i = 0; i < planets.Length; i++)
            {
                FlowSource.AddItem(PlanetName(planets[i]), i);
                FlowDestination.AddItem(PlanetName(planets[i]), i);
            }
            if (FlowDraft.Initialized)
            {
                FlowSource.SelectId(Array.IndexOf(planets, FlowDraft.Source));
                FlowDestination.SelectId(Array.IndexOf(planets, FlowDraft.Destination));
            }
            else if (planets.Length > 1)
                FlowDestination.SelectId(1);
        }
        if (_flowCommodities.Length == 0)
        {
            _flowCommodities = _commodities.Select(c => c.Id).ToArray();
            for (var i = 0; i < _flowCommodities.Length; i++)
                FlowCommodity.AddItem(Loc.GetString(_prototypes.Index<SWGlobalWarCommodityPrototype>(_flowCommodities[i]).Name), i);
        }
        var grants = _state.FlowGrants.Select(g => g.Id).ToArray();
        if (!_flowGrants.SequenceEqual(grants))
        {
            _flowGrants = grants;
            FlowGrantPicker.Clear();
            FlowGrantPicker.AddItem(Loc.GetString("sw-global-war-economy-no-grant"), -1);
            for (var i = 0; i < grants.Length; i++)
            {
                var grant = _state.FlowGrants[i];
                FlowGrantPicker.AddItem(Loc.GetString("sw-global-war-economy-grant-label", ("employee", grant.Employee),
                    ("min", RateText(grant.MinimumRate)), ("max", RateText(grant.MaximumRate))), i);
            }
            var selected = Array.IndexOf(grants, FlowDraft.Grant);
            FlowGrantPicker.SelectId(selected);
            if (FlowDraft.Grant != Guid.Empty && selected < 0)
            {
                FlowDraft.Grant = Guid.Empty;
                selectionLost = true;
            }
        }
        FlowGrantPicker.SetItemText(0, Loc.GetString(_state.CanManageFlows ?
            "sw-global-war-economy-no-grant" : "sw-global-war-economy-select-grant"));
        for (var i = 0; i < _state.FlowGrants.Count; i++)
            FlowGrantPicker.SetItemDisabled(i + 1, _state.FlowGrants[i].Revoked || _state.FlowGrants[i].Expires <= _state.Time);
        if (!_flowWorkers.SequenceEqual(_state.FlowWorkers))
        {
            _flowWorkers = _state.FlowWorkers.ToArray();
            FlowWorker.Clear();
            FlowWorker.AddItem(Loc.GetString("sw-global-war-economy-select-worker"), -1);
            for (var i = 0; i < _flowWorkers.Length; i++)
                FlowWorker.AddItem(_flowWorkers[i].Name, i);
            var selected = Array.FindIndex(_flowWorkers, w => w.User == FlowDraft.Worker?.User &&
                w.Revision == FlowDraft.Worker?.Revision);
            FlowWorker.SelectId(selected);
            if (FlowDraft.Worker != null && selected < 0)
            {
                FlowDraft.Worker = null;
                selectionLost = true;
            }
        }
        _flowRestoring = false;
        RestoreFlowDraft();
        if (selectionLost)
            SetText(FlowResult, Loc.GetString("sw-global-war-economy-selection-lost"));
        foreach (var result in _state.FlowResults)
        {
            if (result.Request == _pendingFlow?.Request)
                ReceiveFlowResult(result);
        }
        var latest = _state.FlowResults.LastOrDefault();
        SetText(FlowHistory, latest == null ? string.Empty : Loc.GetString("sw-global-war-economy-receipt",
            ("request", latest.Request.ToString("N")[..8]), ("result", ResultText(latest))));
        RefreshFlowContext();
        CaptureFlowDraft();
    }

    public void ReceiveFlowResult(SWGlobalWarFlowResult result)
    {
        if (result.Request != _pendingFlow?.Request && result.Request != _pendingGrant?.Request &&
            result.Request != _pendingRevoke?.Request)
            return;
        var wasGrant = result.Request == _pendingGrant?.Request;
        var wasRevoke = result.Request == _pendingRevoke?.Request;
        _pendingFlow = null;
        _pendingGrant = null;
        _pendingRevoke = null;
        if (result.Success && result.Flow != null)
        {
            _editingFlow = result.Flow.Id;
            _editingRevision = result.Flow.Revision;
        }
        SetText(FlowResult, wasGrant && result.Success ? Loc.GetString("sw-global-war-economy-granted") :
            wasRevoke && result.Success ? Loc.GetString("sw-global-war-economy-revoked") : ResultText(result));
        RefreshFlowEditor();
    }

    private static string ResultText(SWGlobalWarFlowResult result)
    {
        return Loc.GetString(result.Success ? "sw-global-war-economy-saved" : result.Failure switch
        {
            SWGlobalWarEconomyFailure.Conflict => "sw-global-war-economy-conflict",
            SWGlobalWarEconomyFailure.NoRoute => "sw-global-war-economy-no-route",
            SWGlobalWarEconomyFailure.InvalidRequest => "sw-global-war-economy-invalid",
            SWGlobalWarEconomyFailure.OfferExceeded => "sw-global-war-economy-offer-exceeded",
            SWGlobalWarEconomyFailure.None or SWGlobalWarEconomyFailure.Denied => "sw-global-war-economy-denied",
            var failure => "sw-global-war-economy-failure-" + failure.ToString().ToLowerInvariant(),
        });
    }

    private bool ReadFlowSettings(out SWGlobalWarFlowSettings settings)
    {
        settings = default!;
        var rate = 0;
        var order = 0;
        string? invalid = null;
        if (FlowSource.SelectedId < 0 || FlowSource.SelectedId >= _flowPlanets.Length ||
            FlowDestination.SelectedId < 0 || FlowDestination.SelectedId >= _flowPlanets.Length ||
            FlowCommodity.SelectedId < 0 || FlowCommodity.SelectedId >= _flowCommodities.Length)
            invalid = "sw-global-war-economy-invalid-select";
        else if (!ReadRate(FlowRate.Text, out rate))
            invalid = "sw-global-war-economy-invalid-rate";
        else if (!int.TryParse(FlowOrder.Text, out order))
            invalid = "sw-global-war-economy-invalid-order";
        if (invalid != null)
        {
            SetText(FlowResult, Loc.GetString(invalid));
            return false;
        }
        settings = new(_flowPlanets[FlowSource.SelectedId], _flowPlanets[FlowDestination.SelectedId],
            _flowCommodities[FlowCommodity.SelectedId], rate, order, FlowEnabled.Pressed);
        return true;
    }

    private static bool ReadRate(string text, out int rate)
    {
        rate = 0;
        if (!decimal.TryParse(text.Replace(',', '.'), NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var value) || value < 0 ||
            value > int.MaxValue / (decimal) SWGlobalWarFlowSettings.QuantaPerUnit)
            return false;
        var quanta = value * SWGlobalWarFlowSettings.QuantaPerUnit;
        rate = (int) decimal.Round(quanta, 0, MidpointRounding.ToEven);
        return true;
    }

    private static string RateText(int quanta) =>
        (quanta / (decimal) SWGlobalWarFlowSettings.QuantaPerUnit).ToString("0.######", CultureInfo.InvariantCulture);
}
