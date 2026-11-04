using System.Linq;
using Content.Shared._SW.GlobalWar;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._SW.GlobalWar.UI;

public sealed partial class SWGlobalWarMarketConsoleMenu
{
    private string? _suggestedDestination;
    private string? _suggestedCommodity;

    private void ConfigureDeliveryHere(string commodity)
    {
        if (_selected is not { } destination || _state == null || FlowPending)
            return;
        _suggestedDestination = destination.Id;
        _suggestedCommodity = commodity;
        RefreshFlowEditor();
        GoodsScroll.Visible = FreightScroll.Visible = false;
        FlowsScroll.Visible = true;
        GrowthScroll.Visible = false;
        var match = _state.Flows.FirstOrDefault(f => f.Settings.Destination == destination && f.Settings.Commodity.Id == commodity);
        if (match != null)
        {
            LoadFlow(match);
            FlowPicker.SelectId(Array.IndexOf(_flowIds, match.Id));
            return;
        }
        _editingFlow = null;
        _editingRevision = -1;
        FlowPicker.SelectId(-1);
        FlowDestination.SelectId(Array.IndexOf(_flowPlanets, destination.Id));
        FlowCommodity.SelectId(Array.IndexOf(_flowCommodities, commodity));
        SetText(FlowCurrent, Loc.GetString("sw-global-war-economy-flow-new"));
        CaptureFlowDraft();
        RefreshFlowContext();
    }
    private void PrepareFlowDraft(SWGlobalWarMarketConsoleState state)
    {
        if (state.Generation == Guid.Empty || state.Assignment == Guid.Empty ||
            FlowDraft.Generation != state.Generation || FlowDraft.Assignment != state.Assignment ||
            FlowDraft.Revision != state.AssignmentRevision)
            ResetFlowEditor();
        FlowDraft.Generation = state.Generation;
        FlowDraft.Assignment = state.Assignment;
        FlowDraft.Revision = state.AssignmentRevision;
        _restoreFlowDraft = _state == null && FlowDraft.Initialized;
        if (!_restoreFlowDraft)
            return;
        _editingFlow = FlowDraft.Flow;
        _editingRevision = FlowDraft.FlowRevision;
        _pendingFlow = FlowDraft.PendingFlow;
        _pendingGrant = FlowDraft.PendingGrant;
        _pendingRevoke = FlowDraft.PendingRevoke;
    }

    private void CaptureFlowDraft()
    {
        if (_flowRestoring || _state == null || _state.Assignment == Guid.Empty || _state.Generation == Guid.Empty)
            return;
        FlowDraft.Initialized = true;
        FlowDraft.Flow = _editingFlow;
        FlowDraft.FlowRevision = _editingRevision;
        FlowDraft.PendingFlow = _pendingFlow;
        FlowDraft.PendingGrant = _pendingGrant;
        FlowDraft.PendingRevoke = _pendingRevoke;
        FlowDraft.Source = Selected(_flowPlanets, FlowSource);
        FlowDraft.Destination = Selected(_flowPlanets, FlowDestination);
        FlowDraft.Commodity = Selected(_flowCommodities, FlowCommodity);
        FlowDraft.Grant = FlowGrantPicker.SelectedId >= 0 && FlowGrantPicker.SelectedId < _flowGrants.Length
            ? _flowGrants[FlowGrantPicker.SelectedId] : Guid.Empty;
        FlowDraft.Worker = FlowWorker.SelectedId >= 0 && FlowWorker.SelectedId < _flowWorkers.Length
            ? _flowWorkers[FlowWorker.SelectedId] : null;
        FlowDraft.Rate = FlowRate.Text;
        FlowDraft.Order = FlowOrder.Text;
        FlowDraft.Enabled = FlowEnabled.Pressed;
        FlowDraft.HeadDecision = FlowHeadDecision.Pressed;
        FlowDraft.Minimum = GrantMinimum.Text;
        FlowDraft.Maximum = GrantMaximum.Text;
        FlowDraft.Minutes = GrantMinutes.Text;
        FlowDraft.Feedback = _displayedText.GetValueOrDefault(FlowResult, string.Empty);
    }

    private void RestoreFlowDraft()
    {
        if (!_restoreFlowDraft)
            return;
        _restoreFlowDraft = false;
        _flowRestoring = true;
        FlowSource.SelectId(Array.IndexOf(_flowPlanets, FlowDraft.Source));
        FlowDestination.SelectId(Array.IndexOf(_flowPlanets, FlowDraft.Destination));
        FlowCommodity.SelectId(Array.IndexOf(_flowCommodities, FlowDraft.Commodity));
        FlowRate.Text = FlowDraft.Rate;
        FlowOrder.Text = FlowDraft.Order;
        FlowEnabled.Pressed = FlowDraft.Enabled;
        FlowHeadDecision.Pressed = FlowDraft.HeadDecision;
        GrantMinimum.Text = FlowDraft.Minimum;
        GrantMaximum.Text = FlowDraft.Maximum;
        GrantMinutes.Text = FlowDraft.Minutes;
        SetText(FlowResult, FlowDraft.Feedback);
        _flowRestoring = false;
    }

    private static string? Selected(string[] choices, OptionButton picker) =>
        picker.SelectedId >= 0 && picker.SelectedId < choices.Length ? choices[picker.SelectedId] : null;

    private void RefreshFlowContext()
    {
        if (_flowRestoring || _state == null)
            return;
        SetText(FlowMode, Loc.GetString(_editingFlow == null ? "sw-global-war-economy-mode-create" :
            "sw-global-war-economy-mode-edit", ("revision", _editingRevision)));
        var current = _state.Flows.FirstOrDefault(f => f.Id == _editingFlow);
        var conflict = current != null && current.Revision != _editingRevision;
        FlowConflictActions.Visible = conflict;
        LoadCurrentFlow.Disabled = ReapplyFlow.Disabled = FlowPending;
        SaveFlow.Disabled = FlowPending || conflict || _editingFlow != null && current == null ||
            !_state.CanManageFlows && !_state.FlowGrants.Any(g => g.Id == FlowDraft.Grant && !g.Revoked && g.Expires > _state.Time);
        SetText(FlowConflict, conflict ? Loc.GetString("sw-global-war-economy-compare",
            ("source", PlanetName(current!.Settings.Source)), ("destination", PlanetName(current.Settings.Destination)),
            ("commodity", Loc.GetString(_prototypes.Index(current.Settings.Commodity).Name)),
            ("rate", RateText(current.Settings.Rate)), ("order", current.Settings.Order),
            ("enabled", Loc.GetString(current.Settings.Enabled ? "sw-global-war-economy-on" : "sw-global-war-economy-off")),
            ("revision", current.Revision)) : string.Empty);
        var selectedGrant = FlowGrantPicker.SelectedId >= 0 && FlowGrantPicker.SelectedId < _flowGrants.Length
            ? _state.FlowGrants.FirstOrDefault(g => g.Id == _flowGrants[FlowGrantPicker.SelectedId]) : null;
        SetText(FlowGrantDetails, selectedGrant is not { } grant ? string.Empty :
            Loc.GetString("sw-global-war-economy-grant-details", ("source", PlanetName(grant.Source)),
                ("destination", PlanetName(grant.Destination)), ("commodity", Loc.GetString(_prototypes.Index<SWGlobalWarCommodityPrototype>(grant.Commodity).Name)),
                ("flow", grant.Flow ?? Loc.GetString("sw-global-war-economy-mode-create")),
                ("minimum", RateText(grant.MinimumRate)), ("maximum", RateText(grant.MaximumRate)),
                ("minOrder", grant.MinimumOrder), ("maxOrder", grant.MaximumOrder),
                ("seconds", Math.Max(0, (int) (grant.Expires - _state.Time).TotalSeconds))));
        RevokeFlow.Disabled = FlowPending || !_state.CanManageFlows || selectedGrant == null || selectedGrant.Revoked;
        var source = Selected(_flowPlanets, FlowSource);
        var commodity = Selected(_flowCommodities, FlowCommodity);
        var offer = _state.NeutralOffers.FirstOrDefault(o => o.Source == source && o.Commodity == commodity);
        if (offer == null || !ReadRate(FlowRate.Text, out var rate))
        {
            SetText(FlowOffer, string.Empty);
            return;
        }
        var previous = current is { Settings.Enabled: true } && current.Settings.Source.Id == source &&
            current.Settings.Commodity.Id == commodity ? current.DesiredRate ??
                current.Settings.Rate / (decimal) SWGlobalWarFlowSettings.QuantaPerUnit : 0;
        var proposed = offer.OwnOrdered - previous + (FlowEnabled.Pressed ? rate / (decimal) SWGlobalWarFlowSettings.QuantaPerUnit : 0);
        SetText(FlowOffer, Loc.GetString("sw-global-war-economy-offer-preview", ("offer", offer.Offer.ToString("0.###")),
            ("ordered", offer.OwnOrdered.ToString("0.###")), ("proposed", proposed.ToString("0.###")),
            ("remaining", Math.Max(0, offer.Offer - proposed).ToString("0.###"))));
    }
}
