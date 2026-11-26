using System.Linq;
using Content.Shared._SW.GlobalWar;

namespace Content.Client._SW.GlobalWar.UI;

public sealed partial class SWGlobalWarMarketConsoleMenu
{
    public event Action<SWGlobalWarSupplyCommand>? SupplyRequested;
    private bool _supplyRefreshing;
    private Guid[] _supplyOrders = [];
    private SWGlobalWarSupplyKind? _supplyRequestedKind;

    private void InitializeSupply()
    {
        InitializeSupplyPurchase();
        InitializeSupplyArsenals();
        foreach (var kind in Enum.GetValues<SWGlobalWarSupplyKind>())
            SupplyKindPicker.AddItem(SupplyName(kind), (int) kind);
        SupplyToggle.OnPressed += _ => SupplyBody.Visible = !SupplyBody.Visible;
        SupplyKindPicker.OnItemSelected += args =>
        {
            if (_supplyRefreshing)
                return;
            FlowDraft.SupplyKind = (SWGlobalWarSupplyKind) args.Id;
            FlowDraft.SupplyRevision = -1;
            FlowDraft.SupplyOrder = Guid.Empty;
            FlowDraft.SupplyPurchaseBacta = false;
            InvalidateSupplyQuote();
            _supplyRequestedKind = null;
            RefreshSupply();
        };
        SupplyLocal.OnTextChanged += _ =>
        {
            if (!_supplyRefreshing)
            {
                FlowDraft.SupplyLocal = SupplyLocal.Text;
                RefreshSupply();
            }
        };
        SupplyBacta.OnToggled += _ =>
        {
            if (!_supplyRefreshing)
                FlowDraft.SupplyBacta = SupplyBacta.Pressed;
        };
        SupplyHeadDecision.OnToggled += _ =>
        {
            if (!_supplyRefreshing)
            {
                FlowDraft.SupplyHead = SupplyHeadDecision.Pressed;
                InvalidateSupplyQuote();
                RefreshSupply();
            }
        };
        SupplyLoad.OnPressed += _ =>
        {
            if (FlowDraft.PendingSupply == null)
            {
                FlowDraft.SupplyRevision = -1;
                RefreshSupply();
            }
        };
        SupplySave.OnPressed += _ =>
        {
            if (SupplySave.Disabled || _state is not { Supply.CanProduce: true } || !int.TryParse(FlowDraft.SupplyLocal, out var local) ||
                local is < 0 or > 100)
                return;
            SendSupply(new(_state.Generation, Guid.NewGuid(), _state.AssignmentRevision,
                SWGlobalWarSupplyAction.ConfigureLine, FlowDraft.SupplyKind, FlowDraft.SupplyRevision,
                LocalPercent: local, Bacta: FlowDraft.SupplyBacta, HeadDecision: FlowDraft.SupplyHead));
        };
        SupplyReapply.OnPressed += _ =>
        {
            if (SupplyReapply.Disabled || _state?.Supply is not { CanProduce: true } ||
                _state.Supply.Lines.FirstOrDefault(l => l.Kind == FlowDraft.SupplyKind) is not { } current)
                return;
            FlowDraft.SupplyRevision = current.Revision;
            RefreshSupply();
            if (int.TryParse(FlowDraft.SupplyLocal, out var local) && local is >= 0 and <= 100)
                SendSupply(new(_state.Generation, Guid.NewGuid(), _state.AssignmentRevision,
                    SWGlobalWarSupplyAction.ConfigureLine, FlowDraft.SupplyKind, current.Revision,
                    LocalPercent: local, Bacta: FlowDraft.SupplyBacta, HeadDecision: FlowDraft.SupplyHead));
        };
        SupplyResume.OnPressed += _ =>
        {
            if (SupplyResume.Disabled || _state?.Supply is not { CanPurchase: true } ||
                _state.Supply.Lines.FirstOrDefault(l => l.Kind == FlowDraft.SupplyKind) is not { Suspended: true } line)
                return;
            SendSupply(new(_state.Generation, Guid.NewGuid(), _state.AssignmentRevision,
                SWGlobalWarSupplyAction.ResumeIntent, line.Kind, Target: line.Intent, HeadDecision: FlowDraft.SupplyHead));
        };
        SupplyOrderPicker.OnItemSelected += args =>
        {
            if (_supplyRefreshing)
                return;
            FlowDraft.SupplyOrder = args.Id >= 0 && args.Id < _supplyOrders.Length ? _supplyOrders[args.Id] : Guid.Empty;
            RefreshSupply();
        };
        SupplyCancel.OnPressed += _ =>
        {
            if (SupplyCancel.Disabled || _state?.Supply is not { CanPurchase: true } ||
                _state.Supply.Orders.FirstOrDefault(o => o.Id == FlowDraft.SupplyOrder) is not { Outstanding: > 0 } order)
                return;
            SendSupply(FlowDraft.SupplyReturnRetries.TryGetValue(order.Id, out var retry) ? retry :
                new(_state.Generation, Guid.NewGuid(), _state.AssignmentRevision,
                SWGlobalWarSupplyAction.CancelPurchase, order.Kind, Target: order.Id, HeadDecision: FlowDraft.SupplyHead));
        };
        SupplyRetry.OnPressed += _ =>
        {
            if (FlowDraft.PendingSupply is { } pending)
                SupplyRequested?.Invoke(pending);
            else if (FlowDraft.SupplyReturnRetries.TryGetValue(FlowDraft.SupplyOrder, out var retry))
                SendSupply(retry);
        };
        SupplyPrevious.OnPressed += _ => RequestSupplyPage(-1);
        SupplyNext.OnPressed += _ => RequestSupplyPage(1);
    }

    private void RequestSupplyPage(int offset)
    {
        if (_state?.Supply is { } view)
            PageRequested?.Invoke(_state.Query with { SupplyPage = Math.Max(0, view.Page + offset), SupplyKind = FlowDraft.SupplyKind });
    }

    private void SendSupply(SWGlobalWarSupplyCommand command)
    {
        if (FlowDraft.PendingSupply != null)
            return;
        FlowDraft.PendingSupply = command;
        FlowDraft.SupplyFeedback = Loc.GetString("sw-global-war-economy-pending");
        SupplyRequested?.Invoke(command);
        RefreshSupply();
    }

    public void ReceiveSupplyResult(SWGlobalWarSupplyResult result)
    {
        if (result.Request != FlowDraft.PendingSupply?.Request)
            return;
        var command = FlowDraft.PendingSupply;
        FlowDraft.PendingSupply = null;
        if (result.Failure == SWGlobalWarSupplyFailure.ReturnPending)
            FlowDraft.SupplyReturnRetries[command.Target] = command;
        else if (result.Success && command.Action is SWGlobalWarSupplyAction.CancelPurchase or SWGlobalWarSupplyAction.CancelProduction)
            FlowDraft.SupplyReturnRetries.Remove(command.Target);
        FlowDraft.SupplySeenResults[result.Request] = result;
        if (result.Success && command.Action == SWGlobalWarSupplyAction.PreviewPurchase)
        {
            FlowDraft.SupplyQuoteRequest = command;
            FlowDraft.SupplyQuote = result.Quote;
        }
        else if (result.Success && command.Action == SWGlobalWarSupplyAction.Purchase)
        {
            FlowDraft.SupplyOrder = result.Target;
            InvalidateSupplyQuote();
        }
        if (result.Success && command.Action == SWGlobalWarSupplyAction.ConfigureLine)
            FlowDraft.SupplyRevision = result.Revision;
        FlowDraft.SupplyFeedback = result.Success
            ? Loc.GetString(command.Action == SWGlobalWarSupplyAction.PreviewPurchase ? "sw-global-war-supply-quoted" :
                result.ProductionContinues ? "sw-global-war-supply-cancel-continued" : "sw-global-war-supply-done")
            : SupplyFailure(result.Failure);
        RefreshSupply();
    }

    private void RefreshSupply()
    {
        if (_supplyRefreshing)
            return;
        _supplyRefreshing = true;
        try
        {
            SupplyPanel.Visible = _state?.Supply != null;
            if (_state?.Supply is not { } view)
            {
                SupplyBody.Visible = false;
                SupplyOrderPicker.Clear();
                SupplyArsenalPicker.Clear();
                SupplyArsenalControls.Visible = false;
                RefreshSupplyPurchase(null);
                foreach (var label in new[] { SupplySummary, SupplyWindow, SupplyPhysical, SupplyFunds, SupplyWaiting,
                             SupplyConflict, SupplyOrderDetails, SupplyBatchDetails, SupplyCancelConsequences, SupplyResult,
                             SupplyServerValues, SupplyLocalError, SupplyAllocation, SupplyOrderPayment, SupplyAllowedArsenals,
                             SupplyArsenalChanged, SupplyOrderTransport, SupplyOrderParts })
                    SetText(label, string.Empty);
                return;
            }
            foreach (var result in _state.SupplyResults)
                if ((!FlowDraft.SupplySeenResults.TryGetValue(result.Request, out var seen) || result != seen) &&
                    result.Request == FlowDraft.PendingSupply?.Request)
                    ReceiveSupplyResult(result);
            var line = view.Lines.First(l => l.Kind == FlowDraft.SupplyKind);
            RefreshSupplyPurchase(view);
            if (view.Kind != line.Kind && _supplyRequestedKind != line.Kind)
            {
                _supplyRequestedKind = line.Kind;
                PageRequested?.Invoke(_state.Query with { SupplyPage = 0, SupplyKind = line.Kind });
            }
            else if (view.Kind == line.Kind)
                _supplyRequestedKind = null;
            if (FlowDraft.SupplyRevision < 0)
            {
                FlowDraft.SupplyRevision = line.Revision;
                FlowDraft.SupplyLocal = line.LocalPercent.ToString();
                FlowDraft.SupplyBacta = line.Bacta;
            }
            SupplyKindPicker.SelectId((int) line.Kind);
            SupplyLocal.Text = FlowDraft.SupplyLocal;
            SupplyBacta.Pressed = FlowDraft.SupplyBacta;
            SupplyHeadDecision.Pressed = FlowDraft.SupplyHead;
            SupplyHeadDecision.Visible = _state.Head;
            var pending = FlowDraft.PendingSupply != null;
            SupplyKindPicker.Disabled = SupplyOrderPicker.Disabled = pending;
            SupplyLineControls.Visible = view.CanProduce;
            SupplyBacta.Visible = line.Kind == SWGlobalWarSupplyKind.Medicine;
            var validLocal = int.TryParse(FlowDraft.SupplyLocal, out var local) && local is >= 0 and <= 100;
            SupplySave.Disabled = pending || !validLocal || line.Revision != FlowDraft.SupplyRevision ||
                view.IndustryHeadDecisionRequired && !FlowDraft.SupplyHead;
            SupplyReapply.Visible = line.Revision != FlowDraft.SupplyRevision;
            SupplyReapply.Disabled = pending || !validLocal || view.IndustryHeadDecisionRequired && !FlowDraft.SupplyHead;
            SetText(SupplyLocalError, validLocal ? string.Empty : Loc.GetString("sw-global-war-supply-invalid-percent"));
            SetText(SupplyServerValues, Loc.GetString("sw-global-war-supply-server-values", ("local", line.LocalPercent),
                ("item", SupplyItem(line.Kind, line.Bacta))));
            SupplyLoad.Disabled = pending;
            SupplyResume.Visible = view.CanPurchase && line.Suspended;
            SupplyResume.Disabled = pending || view.WarHeadDecisionRequired && !FlowDraft.SupplyHead;
            SetText(SupplySummary, Loc.GetString("sw-global-war-supply-summary", ("target", line.Target),
                ("accepted", line.Accepted), ("outstanding", line.Outstanding),
                ("missing", Math.Max(0L, line.Target - line.Accepted - line.Outstanding))));
            SetText(SupplyWindow, line.Window == 0 ? Loc.GetString("sw-global-war-supply-first-window") :
                Loc.GetString("sw-global-war-supply-window", ("start", line.Window * 20), ("end", (line.Window + 1) * 20),
                    ("participants", line.WindowParticipants), ("budget", line.Budget), ("remaining", line.Remaining)));
            SetText(SupplyPhysical, Loc.GetString(line.ConsumerRegistered ? "sw-global-war-supply-physical-required" :
                "sw-global-war-supply-no-consumer"));
            SetText(SupplyFunds, view.WarAvailable is { } money ? Loc.GetString("sw-global-war-supply-funds", ("amount", money)) : string.Empty);
            SetText(SupplyWaiting, line.Suspended ? Loc.GetString("sw-global-war-supply-suspended") :
                line.Failure == SWGlobalWarSupplyFailure.TransportRoute ?
                    Loc.GetString("sw-global-war-supply-auto-route-wait") : SupplyFailure(line.Failure));
            SetText(SupplyConflict, line.Revision != FlowDraft.SupplyRevision ? Loc.GetString("sw-global-war-supply-conflict") : string.Empty);
            var orders = view.Orders.Where(o => o.Kind == line.Kind).ToArray();
            _supplyOrders = orders.Select(o => o.Id).ToArray();
            SupplyOrderPicker.Clear();
            SupplyOrderPicker.AddItem(Loc.GetString("sw-global-war-supply-select-order"), -1);
            for (var i = 0; i < orders.Length; i++)
                SupplyOrderPicker.AddItem(Loc.GetString("sw-global-war-supply-order-title",
                    ("id", orders[i].Number), ("count", orders[i].Quantity),
                    ("item", SupplyItem(orders[i].Kind, orders[i].Bacta))), i);
            SupplyOrderPicker.SelectId(Array.IndexOf(_supplyOrders, FlowDraft.SupplyOrder));
            SupplyPrevious.Disabled = view.Kind != line.Kind || view.Page == 0;
            SupplyNext.Disabled = view.Kind != line.Kind ||
                (view.Page + 1) * SWGlobalWarMarketConsoleState.ShipmentPageSize >= view.TotalOrders;
            var order = orders.FirstOrDefault(o => o.Id == FlowDraft.SupplyOrder);
            RefreshSupplyArsenals(view, order, pending);
            SetText(SupplyOrderTransport, string.IsNullOrEmpty(order?.TransportHold) ? string.Empty : Loc.GetString(order.TransportHold));
            SetText(SupplyOrderParts, order?.Parts is not { Count: > 0 } parts ? string.Empty :
                Loc.GetString("sw-global-war-supply-order-parts", ("number", order.Number),
                    ("parts", string.Join(", ", parts.Select(p => p.ToString()[..8])))));
            SetText(SupplyOrderDetails, order == null ? string.Empty : Loc.GetString("sw-global-war-supply-order-detail",
                ("item", SupplyItem(order.Kind, order.Bacta)),
                ("source", PlanetName(order.Source)), ("recipient", PlanetName(order.Recipient)),
                ("accepted", order.Accepted), ("outstanding", order.Outstanding), ("cancelled", order.Cancelled),
                ("state", SupplyStage(order.Stage))));
            SetText(SupplyBatchDetails, order?.Batch is not { } batch ? string.Empty :
                Loc.GetString("sw-global-war-supply-batch", ("id", batch.Id.ToString("N")[..8]), ("quantity", batch.Quantity),
                    ("market", batch.MarketQuantity), ("local", batch.LocalQuantity), ("seconds", (int) batch.Duration.TotalSeconds),
                    ("state", SupplyStage(batch.Stage)), ("linked", Loc.GetString(batch.Purchase == order.Id ?
                        "sw-global-war-supply-linked" : "sw-global-war-supply-detached"))));
            SetText(SupplyAllocation, order == null ? string.Empty :
                Loc.GetString("sw-global-war-supply-initial-allocation", ("ready", order.InitialReady),
                    ("production", order.InitialProduction)) + (order.Batch is not { } allocation ? string.Empty : "\n" +
                    (allocation.FreeMarket is not { } freeMarket || allocation.FreeLocal is not { } freeLocal
                        ? Loc.GetString("sw-global-war-supply-output-unknown")
                        : Loc.GetString(allocation.FreeIsPlanned ? "sw-global-war-supply-planned-allocation" :
                            "sw-global-war-supply-actual-allocation", ("assigned", allocation.AssignedToOrder),
                            ("market", freeMarket), ("local", freeLocal)))));
            SetText(SupplyOrderPayment, order?.ReservedPayment is { } payment ?
                Loc.GetString("sw-global-war-supply-order-payment", ("amount", payment)) : string.Empty);
            SetText(SupplyCancelConsequences, order == null || order.Outstanding == 0 ? string.Empty :
                order.ReturnPending ? SupplyFailure(SWGlobalWarSupplyFailure.ReturnPending) :
                Loc.GetString(order.Batch?.Stage == SWGlobalWarSupplyStage.Producing ?
                    "sw-global-war-supply-cancel-producing" : "sw-global-war-supply-cancel-unused"));
            SupplyCancel.Visible = view.CanPurchase && order is { Outstanding: > 0 };
            SupplyCancel.Text = order?.ReturnPending == true ? Loc.GetString("sw-global-war-supply-check-return") :
                Loc.GetString("sw-global-war-supply-cancel-count", ("count", order?.Outstanding ?? 0));
            SupplyCancel.Disabled = pending || view.WarHeadDecisionRequired && !FlowDraft.SupplyHead;
            var retryTarget = FlowDraft.PendingSupply?.Target ?? FlowDraft.SupplyOrder;
            SupplyRetry.Visible = pending || FlowDraft.SupplyReturnRetries.ContainsKey(FlowDraft.SupplyOrder);
            SupplyRetry.Text = view.Orders.FirstOrDefault(o => o.Id == retryTarget) is not { } retryOrder ?
                Loc.GetString("sw-global-war-supply-retry") :
                Loc.GetString("sw-global-war-supply-retry-order", ("id", retryOrder.Number));
            SetText(SupplyResult, FlowDraft.SupplyFeedback);
        }
        finally
        {
            _supplyRefreshing = false;
        }
    }

    private static string SupplyName(SWGlobalWarSupplyKind kind) => Loc.GetString("sw-global-war-supply-kind-" + kind.ToString().ToLowerInvariant());
    private static string SupplyItem(SWGlobalWarSupplyKind kind, bool bacta) => kind != SWGlobalWarSupplyKind.Medicine
        ? SupplyName(kind) : Loc.GetString(bacta ? "sw-global-war-supply-bacta-item" : "sw-global-war-supply-kolto-item");
    private static string SupplyStage(SWGlobalWarSupplyStage stage) => Loc.GetString("sw-global-war-supply-stage-" + stage.ToString().ToLowerInvariant());
    private static string SupplyFailure(SWGlobalWarSupplyFailure failure) => failure == SWGlobalWarSupplyFailure.None
        ? string.Empty : Loc.GetString("sw-global-war-supply-failure-" + failure.ToString().ToLowerInvariant());
}
