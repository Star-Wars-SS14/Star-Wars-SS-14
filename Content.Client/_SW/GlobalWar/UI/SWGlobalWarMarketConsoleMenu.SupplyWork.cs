using System.Linq;
using Content.Shared._SW.GlobalWar;

namespace Content.Client._SW.GlobalWar.UI;

public sealed partial class SWGlobalWarMarketConsoleMenu
{
    public event Action<SWGlobalWarSupplyWorkCommand>? SupplyWorkRequested;
    private bool _supplyWorkRefreshing;
    private SWGlobalWarSupplyDeliveryView[] _supplyDeliveries = [];
    private Guid[] _supplyWorkIds = [];
    private NetEntity[] _supplyWorkCarriers = [];

    private void InitializeSupplyWork()
    {
        SupplyWorkToggle.OnPressed += _ => SupplyWorkBody.Visible = !SupplyWorkBody.Visible;
        SupplyDeliveryPicker.OnItemSelected += args =>
        {
            if (_supplyWorkRefreshing || args.Id < 0 || args.Id >= _supplyDeliveries.Length)
                return;
            var delivery = _supplyDeliveries[args.Id];
            FlowDraft.SupplyDelivery = delivery.Purchase;
            FlowDraft.SupplyDeliveryCarrier = delivery.Carrier;
            RefreshSupplyWork();
        };
        SupplyWorkPicker.OnItemSelected += args =>
        {
            if (!_supplyWorkRefreshing && args.Id >= 0 && args.Id < _supplyWorkIds.Length)
            {
                FlowDraft.SupplyWork = _supplyWorkIds[args.Id];
                RefreshSupplyWork();
            }
        };
        SupplyWorkCarrierPicker.OnItemSelected += args =>
        {
            if (!_supplyWorkRefreshing && args.Id >= 0 && args.Id < _supplyWorkCarriers.Length)
            {
                FlowDraft.SupplyWorkCarrier = _supplyWorkCarriers[args.Id];
                RefreshSupplyWork();
            }
        };
        SupplyWorkQuantity.OnTextChanged += _ =>
        {
            if (!_supplyWorkRefreshing)
            {
                FlowDraft.SupplyWorkQuantity = SupplyWorkQuantity.Text;
                RefreshSupplyWork();
            }
        };
        SupplyWorkHead.OnToggled += _ =>
        {
            if (!_supplyWorkRefreshing)
            {
                FlowDraft.SupplyWorkHead = SupplyWorkHead.Pressed;
                RefreshSupplyWork();
            }
        };
        SupplyWorkClaim.OnPressed += _ => SendWork(SWGlobalWarSupplyWorkAction.Claim, SupplyWorkClaim.Disabled);
        SupplyWorkCollect.OnPressed += _ => SendWork(SWGlobalWarSupplyWorkAction.Collect, SupplyWorkCollect.Disabled);
        SupplyWorkAccept.OnPressed += _ => SendWork(SWGlobalWarSupplyWorkAction.Accept, SupplyWorkAccept.Disabled);
        SupplyWorkRelease.OnPressed += _ => SendWork(SWGlobalWarSupplyWorkAction.Release, SupplyWorkRelease.Disabled);
        SupplyWorkRevoke.OnPressed += _ => SendWork(SWGlobalWarSupplyWorkAction.Revoke, SupplyWorkRevoke.Disabled);
        SupplyWorkRetry.OnPressed += _ =>
        {
            if (FlowDraft.PendingSupplyWork is { } pending)
                SupplyWorkRequested?.Invoke(pending);
        };
        SupplyWorkPrevious.OnPressed += _ => RequestWorkPage(-1);
        SupplyWorkNext.OnPressed += _ => RequestWorkPage(1);
    }

    private void RequestWorkPage(int delta)
    {
        if (_state?.SupplyWork is { } work)
            PageRequested?.Invoke(_state.Query with { SupplyWorkPage = Math.Max(0, work.Page + delta) });
    }

    private void SendWork(SWGlobalWarSupplyWorkAction action, bool disabled)
    {
        if (disabled || _state == null || FlowDraft.PendingSupplyWork != null)
            return;
        var claim = action == SWGlobalWarSupplyWorkAction.Claim;
        var command = new SWGlobalWarSupplyWorkCommand(_state.Generation, Guid.NewGuid(), _state.AssignmentRevision,
            action, claim ? FlowDraft.SupplyDelivery : FlowDraft.SupplyWork,
            claim && int.TryParse(FlowDraft.SupplyWorkQuantity, out var quantity) ? quantity : 1,
            claim ? FlowDraft.SupplyDeliveryCarrier : FlowDraft.SupplyWorkCarrier, FlowDraft.SupplyWorkHead);
        FlowDraft.PendingSupplyWork = command;
        FlowDraft.SupplyWorkFeedback = Loc.GetString("sw-global-war-economy-pending");
        SupplyWorkRequested?.Invoke(command);
        RefreshSupplyWork();
    }

    public void ReceiveSupplyWorkResult(SWGlobalWarSupplyWorkResult result)
    {
        if (FlowDraft.PendingSupplyWork is not { } pending || pending.Request != result.Request)
            return;
        FlowDraft.SupplyWorkSeenResults[result.Request] = result;
        FlowDraft.PendingSupplyWork = null;
        if (result.Success)
        {
            FlowDraft.SupplyWork = result.Work;
            if (pending.Action == SWGlobalWarSupplyWorkAction.Claim && pending.Carrier != null)
                FlowDraft.SupplyWorkCarrier = pending.Carrier;
        }
        FlowDraft.SupplyWorkFeedback = result.Success ? Loc.GetString("sw-global-war-supply-work-success") :
            result.Failure == SWGlobalWarSupplyFailure.Transfer ? Loc.GetString("sw-global-war-supply-work-transfer-failed") :
            SupplyFailure(result.Failure);
        if (!_supplyWorkRefreshing)
            RefreshSupplyWork();
    }

    private void RefreshSupplyWork()
    {
        if (_supplyWorkRefreshing)
            return;
        _supplyWorkRefreshing = true;
        try
        {
            var board = _state?.SupplyWork;
            SupplyWorkPanel.Visible = board != null;
            SupplyDeliveryPicker.Clear();
            SupplyWorkPicker.Clear();
            SupplyWorkCarrierPicker.Clear();
            if (board == null)
            {
                _supplyDeliveries = [];
                _supplyWorkIds = [];
                _supplyWorkCarriers = [];
                foreach (var label in new[] { SupplyDeliveryDetails, SupplyWorkDetails, SupplyWorkQuantityError, SupplyWorkFeedback })
                    SetText(label, string.Empty);
                return;
            }
            foreach (var result in _state!.SupplyWorkResults)
                if (!FlowDraft.SupplyWorkSeenResults.ContainsKey(result.Request))
                    ReceiveSupplyWorkResult(result);
            var pending = FlowDraft.PendingSupplyWork != null;
            _supplyWorkCarriers = board.Carriers.Select(c => c.Entity).ToArray();
            SupplyWorkCarrierPicker.AddItem(Loc.GetString("sw-global-war-supply-work-select-carrier"), -1);
            for (var i = 0; i < board.Carriers.Count; i++)
                SupplyWorkCarrierPicker.AddItem(board.Carriers[i].Name, i);
            SupplyWorkCarrierPicker.SelectId(FlowDraft.SupplyWorkCarrier is { } carrier ?
                Array.IndexOf(_supplyWorkCarriers, carrier) : -1);
            var selectedCarrier = SupplyWorkCarrierPicker.SelectedId >= 0;
            _supplyDeliveries = board.Deliveries.ToArray();
            SupplyDeliveryPicker.AddItem(Loc.GetString("sw-global-war-supply-work-select-delivery"), -1);
            for (var i = 0; i < _supplyDeliveries.Length; i++)
            {
                var item = _supplyDeliveries[i];
                SupplyDeliveryPicker.AddItem(Loc.GetString("sw-global-war-supply-work-delivery",
                    ("item", SupplyItem(item.Kind, item.Bacta)), ("count", item.Available),
                    ("number", item.Number)), i);
            }
            var selected = Array.FindIndex(_supplyDeliveries, d => d.Purchase == FlowDraft.SupplyDelivery &&
                d.Carrier == FlowDraft.SupplyDeliveryCarrier);
            SupplyDeliveryPicker.SelectId(selected);
            var delivery = selected >= 0 ? _supplyDeliveries[selected] : null;
            SetText(SupplyDeliveryDetails, delivery == null ? string.Empty : Loc.GetString("sw-global-war-supply-work-route",
                ("number", delivery.Number), ("source", PlanetName(delivery.Source)), ("sourceName", delivery.SourceName),
                ("recipient", PlanetName(delivery.Recipient)), ("recipientName", delivery.RecipientName),
                ("status", Loc.GetString(delivery.Carrier == null ?
                    "sw-global-war-supply-work-at-source" : "sw-global-war-supply-work-recover"))));
            SupplyWorkQuantity.Text = FlowDraft.SupplyWorkQuantity;
            var validQuantity = int.TryParse(FlowDraft.SupplyWorkQuantity, out var quantity) && quantity is >= 1 and <= 120;
            var ownActive = board.Work.Any(w => w.Own && w.Status is
                SWGlobalWarSupplyWorkStatus.Assigned or SWGlobalWarSupplyWorkStatus.Collected);
            SetText(SupplyWorkQuantityError, !validQuantity ? Loc.GetString("sw-global-war-supply-invalid-quantity") :
                ownActive ? SupplyFailure(SWGlobalWarSupplyFailure.WorkBusy) :
                delivery != null && quantity > delivery.Available ?
                    Loc.GetString("sw-global-war-supply-work-quantity-remaining", ("count", delivery.Available)) :
                    delivery == null && FlowDraft.SupplyDelivery != Guid.Empty ?
                        SupplyFailure(SWGlobalWarSupplyFailure.QuantityChanged) : string.Empty);
            SupplyWorkClaim.Disabled = pending || delivery == null || !validQuantity || quantity > delivery.Available || ownActive;
            _supplyWorkIds = board.Work.Select(w => w.Id).ToArray();
            SupplyWorkPicker.AddItem(Loc.GetString("sw-global-war-supply-work-select-work"), -1);
            for (var i = 0; i < board.Work.Count; i++)
            {
                var work = board.Work[i];
                SupplyWorkPicker.AddItem(Loc.GetString("sw-global-war-supply-work-row",
                    ("item", SupplyItem(work.Kind, work.Bacta)), ("count", work.Quantity), ("worker", work.Worker),
                    ("number", work.Number)), i);
            }
            SupplyWorkPicker.SelectId(Array.IndexOf(_supplyWorkIds, FlowDraft.SupplyWork));
            var active = board.Work.FirstOrDefault(w => w.Id == FlowDraft.SupplyWork);
            SetText(SupplyWorkDetails, active == null ? string.Empty : Loc.GetString("sw-global-war-supply-work-details",
                ("source", PlanetName(active.Source)), ("recipient", PlanetName(active.Recipient)),
                ("sourceName", active.SourceName), ("recipientName", active.RecipientName), ("number", active.Number),
                ("item", SupplyItem(active.Kind, active.Bacta)), ("count", active.Quantity),
                ("requirements", SupplyWorkRequirements(active.Kind)),
                ("status", active.ReturnPending ? SupplyFailure(SWGlobalWarSupplyFailure.ReturnPending) : WorkStatus(active.Status))));
            var unfinished = active?.Status is SWGlobalWarSupplyWorkStatus.Assigned or SWGlobalWarSupplyWorkStatus.Collected;
            SupplyWorkCollect.Disabled = pending || !selectedCarrier || active is not { Own: true, Status: SWGlobalWarSupplyWorkStatus.Assigned };
            SupplyWorkAccept.Disabled = pending || !selectedCarrier || active is not { Own: true, Status: SWGlobalWarSupplyWorkStatus.Collected };
            SupplyWorkRelease.Disabled = pending || active?.Own != true || !unfinished;
            SupplyWorkRevoke.Visible = board.CanRevoke;
            SupplyWorkRevoke.Disabled = pending || !unfinished || board.HeadDecisionRequired && !FlowDraft.SupplyWorkHead;
            SupplyWorkHead.Visible = board.CanRevoke && _state.Head;
            SupplyWorkHead.Pressed = FlowDraft.SupplyWorkHead;
            SupplyDeliveryPicker.Disabled = SupplyWorkPicker.Disabled = SupplyWorkHead.Disabled = pending;
            SupplyWorkCarrierPicker.Disabled = pending;
            SupplyWorkQuantity.Editable = !pending;
            SupplyWorkRetry.Visible = pending;
            SupplyWorkPrevious.Disabled = pending || board.Page == 0;
            SupplyWorkNext.Disabled = pending || (board.Page + 1) * 40 >= board.Total;
            SetText(SupplyWorkFeedback, FlowDraft.SupplyWorkFeedback);
        }
        finally { _supplyWorkRefreshing = false; }
    }

    private static string WorkStatus(SWGlobalWarSupplyWorkStatus status)
    {
        return Loc.GetString(status switch
        {
            SWGlobalWarSupplyWorkStatus.Assigned => "sw-global-war-supply-work-assigned",
            SWGlobalWarSupplyWorkStatus.Collected => "sw-global-war-supply-work-collected",
            SWGlobalWarSupplyWorkStatus.Completed => "sw-global-war-supply-work-completed",
            SWGlobalWarSupplyWorkStatus.Released => "sw-global-war-supply-work-released",
            _ => "sw-global-war-supply-work-revoked",
        });
    }

    private static string SupplyWorkRequirements(SWGlobalWarSupplyKind kind)
    {
        return Loc.GetString(kind switch
        {
            SWGlobalWarSupplyKind.Magazine => "sw-global-war-supply-work-require-magazine",
            SWGlobalWarSupplyKind.Medicine => "sw-global-war-supply-work-require-medicine",
            SWGlobalWarSupplyKind.Ration => "sw-global-war-supply-work-require-ration",
            _ => "sw-global-war-supply-work-require-equipment",
        });
    }
}
