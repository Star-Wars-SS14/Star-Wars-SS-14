using System.Linq;
using Content.Shared._SW.GlobalWar;

namespace Content.Client._SW.GlobalWar.UI;

public sealed partial class SWGlobalWarMarketConsoleMenu
{
    public event Action<SWGlobalWarReturnCommand>? ReturnRequested;
    private string[] _returnDestinations = [];
    private string[] _returnGrantIds = [];
    private string[] _returnWorkerIds = [];
    private (Guid, Guid, long)? _returnScope;

    private void InitializeReturns()
    {
        ReturnDestination.OnItemSelected += args =>
        {
            if (FlowDraft.PendingReturn != null)
                return;
            ReturnDestination.SelectId(args.Id);
            var key = Selected(_returnDestinations, ReturnDestination);
            var destination = _state?.ReturnCandidates.FirstOrDefault(c => c.Part.Shipment == FlowDraft.ReturnPart?.Shipment)?
                .Destinations.FirstOrDefault(d => ReturnDestinationKey(d.Planet, d.Store) == key);
            FlowDraft.ReturnDestination = destination?.Planet;
            FlowDraft.ReturnStore = destination?.Store;
            FlowDraft.ReturnFeedback = string.Empty;
            RefreshReturns();
        };
        ReturnHead.OnToggled += _ => { FlowDraft.ReturnHead = ReturnHead.Pressed; RefreshReturns(); };
        ReturnMinutes.OnTextChanged += _ => FlowDraft.ReturnMinutes = ReturnMinutes.Text;
        ReturnWorker.OnItemSelected += args =>
        {
            ReturnWorker.SelectId(args.Id);
            var key = Selected(_returnWorkerIds, ReturnWorker);
            FlowDraft.ReturnWorker = _state?.FlowWorkers.FirstOrDefault(w => WorkerKey(w) == key);
            RefreshReturns();
        };
        ReturnGrantPicker.OnItemSelected += args =>
        {
            ReturnGrantPicker.SelectId(args.Id);
            FlowDraft.ReturnGrant = Guid.TryParse(Selected(_returnGrantIds, ReturnGrantPicker), out var id) ? id : Guid.Empty;
            RefreshReturns();
        };
        ReturnReload.OnPressed += _ =>
        {
            if (FlowDraft.ReturnPart is { } part)
                SelectReturn(part.Shipment);
        };
        ReturnSend.OnPressed += _ => SendReturn(SWGlobalWarReturnAction.Redirect);
        ReturnGrant.OnPressed += _ => SendReturn(SWGlobalWarReturnAction.Grant);
        ReturnRevoke.OnPressed += _ => SendReturn(SWGlobalWarReturnAction.Revoke);
        ReturnRetry.OnPressed += _ =>
        {
            if (FlowDraft.PendingReturn is { } pending)
                ReturnRequested?.Invoke(pending);
        };
    }

    private void SelectReturn(Guid shipment)
    {
        if (FlowDraft.PendingReturn != null || _state?.ReturnCandidates.FirstOrDefault(c => c.Part.Shipment == shipment) is not { } candidate)
            return;
        FlowDraft.ReturnPart = candidate.Part;
        FlowDraft.ReturnFeedback = string.Empty;
        ShowPage(true);
        RefreshReturns();
        FreightScroll.SetScrollValue(default);
    }

    private void SendReturn(SWGlobalWarReturnAction action)
    {
        if (_state == null || FlowDraft.PendingReturn != null)
            return;
        var part = FlowDraft.ReturnPart;
        var destination = FlowDraft.ReturnDestination;
        var destinationStore = FlowDraft.ReturnStore;
        var grant = _state.ReturnGrants.FirstOrDefault(g => g.Id == FlowDraft.ReturnGrant);
        if (action == SWGlobalWarReturnAction.Revoke && grant != null)
        {
            part = grant.Part;
            destination = grant.Destination;
            destinationStore = grant.DestinationStore;
        }
        var minutes = 10;
        if (part == null || destination == null || action == SWGlobalWarReturnAction.Revoke && grant == null ||
            action == SWGlobalWarReturnAction.Grant && (FlowDraft.ReturnWorker == null ||
                !int.TryParse(ReturnMinutes.Text, out minutes) || minutes <= 0))
        {
            FlowDraft.ReturnFeedback = ReturnFailureText(
                action == SWGlobalWarReturnAction.Revoke && grant == null ? SWGlobalWarReturnFailure.NoGrant :
                part == null ? SWGlobalWarReturnFailure.NoPart :
                destination == null ? SWGlobalWarReturnFailure.NoDestination :
                FlowDraft.ReturnWorker == null ? SWGlobalWarReturnFailure.NoWorker : SWGlobalWarReturnFailure.InvalidDuration);
            RefreshReturns();
            return;
        }
        // Keep the selected physical scope even if an intervening snapshot has newer data.
        if (action != SWGlobalWarReturnAction.Revoke &&
            _state.ReturnCandidates.FirstOrDefault(c => c.Part.Shipment == part.Shipment)?.Part != part)
        {
            FlowDraft.ReturnFeedback = ReturnFailureText(SWGlobalWarReturnFailure.Changed);
            RefreshReturns();
            return;
        }
        FlowDraft.PendingReturn = new(_state.Generation, Guid.NewGuid(), _state.AssignmentRevision, action, part,
            destination, FlowDraft.ReturnGrant, FlowDraft.ReturnWorker?.User, FlowDraft.ReturnWorker?.Revision ?? 0,
            minutes, ReturnHead.Pressed, destinationStore);
        FlowDraft.ReturnFeedback = Loc.GetString("sw-global-war-return-pending");
        ReturnRequested?.Invoke(FlowDraft.PendingReturn);
        RefreshReturns();
    }

    public void ReceiveReturnResult(SWGlobalWarReturnResult result)
    {
        if (result.Request != FlowDraft.PendingReturn?.Request)
            return;
        FlowDraft.PendingReturn = null;
        FlowDraft.ReturnFeedback = result.Success ? Loc.GetString(result.Action switch
        {
            SWGlobalWarReturnAction.Grant => "sw-global-war-economy-granted",
            SWGlobalWarReturnAction.Revoke => "sw-global-war-economy-revoked",
            _ => "sw-global-war-return-accepted",
        }) : ReturnFailureText(result.Failure);
        if (result.Receipt != null)
            FlowDraft.LastReturnRequest = result.Request;
        RefreshReturns();
        if (result.Receipt != null)
            ShowReturnReceipt(result.Receipt);
    }

    private void RefreshReturns()
    {
        if (_state == null)
            return;
        var scope = (_state.Generation, _state.Assignment, _state.AssignmentRevision);
        if (_returnScope != scope)
        {
            _returnScope = scope;
            ReturnHead.Pressed = FlowDraft.ReturnHead;
            ReturnMinutes.Text = FlowDraft.ReturnMinutes;
        }
        foreach (var result in _state.ReturnResults)
            if (result.Request == FlowDraft.PendingReturn?.Request)
                ReceiveReturnResult(result);
        var current = _state.ReturnCandidates.FirstOrDefault(c => c.Part.Shipment == FlowDraft.ReturnPart?.Shipment);
        var pending = FlowDraft.PendingReturn != null;
        var changed = current != null && current.Part != FlowDraft.ReturnPart;
        var grants = _state.ReturnGrants.Where(g => g.Part.Shipment == FlowDraft.ReturnPart?.Shipment).ToArray();
        UpdateGrowthPicker(ReturnDestination, ref _returnDestinations,
            current?.Destinations.Select(d => ReturnDestinationKey(d.Planet, d.Store)).ToArray() ?? [],
            FlowDraft.ReturnDestination is { } selectedPlanet ? ReturnDestinationKey(selectedPlanet, FlowDraft.ReturnStore) : null,
            "sw-global-war-return-select-destination", id =>
            {
                var target = current!.Destinations.Single(d => ReturnDestinationKey(d.Planet, d.Store) == id);
                return target.Store == null ? PlanetName(target.Planet) : Loc.GetString(target.AcceptsSupply ?
                    "sw-global-war-supply-return-arsenal" : "sw-global-war-supply-return-storage",
                    ("point", target.Name), ("planet", PlanetName(target.Planet)));
            });
        UpdateGrowthPicker(ReturnGrantPicker, ref _returnGrantIds, grants.Select(g => g.Id.ToString()).ToArray(),
            FlowDraft.ReturnGrant.ToString(), _state.CanManageFlows ? "sw-global-war-economy-no-grant" : "sw-global-war-economy-select-grant",
            id => grants.Single(g => g.Id.ToString() == id).Employee);
        UpdateGrowthPicker(ReturnWorker, ref _returnWorkerIds, _state.FlowWorkers.Select(WorkerKey).ToArray(),
            FlowDraft.ReturnWorker is { } worker ? WorkerKey(worker) : null, "sw-global-war-economy-select-worker",
            id => _state.FlowWorkers.Single(w => WorkerKey(w) == id).Name);
        if (FlowDraft.ReturnWorker != null && ReturnWorker.SelectedId < 0)
        {
            FlowDraft.ReturnWorker = null;
            FlowDraft.ReturnFeedback = Loc.GetString("sw-global-war-economy-selection-lost");
        }
        if (FlowDraft.ReturnGrant != Guid.Empty && ReturnGrantPicker.SelectedId < 0)
            FlowDraft.ReturnGrant = Guid.Empty;
        var grant = grants.FirstOrDefault(g => g.Id == FlowDraft.ReturnGrant);
        var destination = current?.Destinations.FirstOrDefault(d => d.Planet == FlowDraft.ReturnDestination &&
            d.Store == FlowDraft.ReturnStore);
        var authority = _state.LogisticsHeadDecisionRequired && !ReturnHead.Pressed ? SWGlobalWarReturnFailure.HeadDecisionRequired :
            _state.CanManageFlows ? SWGlobalWarReturnFailure.None : grant == null ? SWGlobalWarReturnFailure.Denied :
            grant.Part != FlowDraft.ReturnPart || grant.Destination != FlowDraft.ReturnDestination ||
            grant.DestinationStore != FlowDraft.ReturnStore ? SWGlobalWarReturnFailure.Denied : grant.Failure;
        var failure = current == null || changed ? SWGlobalWarReturnFailure.Changed :
            current.Failure != SWGlobalWarReturnFailure.None ? current.Failure :
            destination == null ? SWGlobalWarReturnFailure.InvalidDestination :
            destination.Failure != SWGlobalWarReturnFailure.None ? destination.Failure : authority;
        ReturnPanel.Visible = FlowDraft.ReturnPart != null || pending || FlowDraft.LastReturnRequest != null;
        ReturnHead.Visible = _state.Head;
        ReturnDelegation.Visible = _state.CanManageFlows;
        ReturnReload.Visible = changed;
        ReturnReload.Disabled = pending;
        ReturnDestination.Disabled = pending || current == null;
        ReturnSend.Disabled = pending || failure != SWGlobalWarReturnFailure.None;
        ReturnSend.Text = current != null && destination != null ? Loc.GetString("sw-global-war-return-send",
            ("quantity", ReturnUnits(current.Part.Quantity)),
            ("planet", destination.Store != null ? destination.Name : PlanetName(destination.Planet))) : Loc.GetString("sw-global-war-return-configure");
        ReturnRetry.Disabled = !pending;
        ReturnGrant.Disabled = pending || changed || current == null || current.Failure != SWGlobalWarReturnFailure.None ||
            destination == null || destination.Failure != SWGlobalWarReturnFailure.None ||
            _state.LogisticsHeadDecisionRequired && !ReturnHead.Pressed || FlowDraft.ReturnWorker == null;
        ReturnRevoke.Disabled = pending || grant == null || grant.Failure is SWGlobalWarReturnFailure.GrantRevoked or SWGlobalWarReturnFailure.GrantUsed ||
            _state.LogisticsHeadDecisionRequired && !ReturnHead.Pressed;
        SetText(ReturnDetails, current == null ? Loc.GetString("sw-global-war-return-unavailable") :
            Loc.GetString("sw-global-war-return-details", ("commodity", Loc.GetString(_prototypes.Index<SWGlobalWarCommodityPrototype>(current.Commodity).Name)),
                ("id", current.Part.Shipment.ToString()[..8]), ("quantity", ReturnUnits(current.Part.Quantity)),
                ("node", PlanetName(current.Part.Node)), ("destination", PlanetName(current.Part.OldDestination)),
                ("owner", Loc.GetString(current.Part.Owner == SWGlobalWarFaction.Republic ? "sw-global-war-republic" : "sw-global-war-empire")),
                ("hold", Loc.GetString(current.HoldReason.Length > 0 ? current.HoldReason : "sw-global-war-market-shipment-waiting"))));
        SetText(ReturnScope, grant != null ? Loc.GetString("sw-global-war-return-scope", ("employee", grant.Employee),
            ("quantity", ReturnUnits(grant.Part.Quantity)), ("node", PlanetName(grant.Part.Node)),
            ("destination", ReturnTargetLabel(grant.Destination, grant.Target)), ("seconds", Math.Max(0, (int) (grant.Expires - _state.Time).TotalSeconds)),
            ("status", ReturnFailureText(grant.Failure))) : _state.CanManageFlows ?
                Loc.GetString(_state.Head ? "sw-global-war-return-authority-head" : "sw-global-war-return-authority-minister") : string.Empty);
        SetText(ReturnReason, FlowDraft.LastReturnRequest != null && current == null && !pending
            ? string.Empty : ReturnFailureText(failure));
        SetText(ReturnResult, FlowDraft.ReturnFeedback);
        var receipt = _state.ReturnResults.FirstOrDefault(r => r.Request == FlowDraft.LastReturnRequest)?.Receipt;
        if (receipt != null)
            ShowReturnReceipt(receipt);
        else
            SetText(ReturnReceipt, string.Empty);
    }

    private void ShowReturnReceipt(SWGlobalWarReturnReceiptView receipt)
    {
        SetText(ReturnReceipt, Loc.GetString("sw-global-war-return-receipt", ("id", receipt.Part.Shipment.ToString()[..8]),
            ("quantity", ReturnUnits(receipt.Part.Quantity)), ("destination", ReturnTargetLabel(receipt.Destination, receipt.Target)),
            ("received", ReturnUnits(receipt.Received)), ("captured", ReturnUnits(receipt.Captured)),
            ("redirected", ReturnUnits(receipt.Redirected))));
    }

    private static string ReturnDestinationKey(string planet, NetEntity? store)
    {
        return store == null ? planet : $"{planet}:{store}";
    }

    private string ReturnTargetLabel(string planet, SWGlobalWarReturnTarget? target)
    {
        return target == null ? PlanetName(planet) : Loc.GetString(target.AcceptsSupply ?
            "sw-global-war-return-target-arsenal" : "sw-global-war-return-target-storage",
            ("point", target.Name), ("planet", PlanetName(planet)));
    }

    internal static string ReturnUnits(int quantity)
    {
        var denominator = 60000;
        var a = quantity;
        var b = denominator;
        while (b != 0)
            (a, b) = (b, a % b);
        denominator /= a;
        // Factors other than two and five require a recurring decimal; the reduced fraction is exact.
        var factors = denominator;
        while (factors % 2 == 0)
            factors /= 2;
        while (factors % 5 == 0)
            factors /= 5;
        return factors == 1 ? (quantity / 60000m).ToString("0.#####") : $"{quantity / a}/{denominator}";
    }
    private static string ReturnFailureText(SWGlobalWarReturnFailure failure) => failure == SWGlobalWarReturnFailure.None
        ? string.Empty : Loc.GetString("sw-global-war-return-failure-" + failure.ToString().ToLowerInvariant());
}
