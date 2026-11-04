using System.Linq;
using Content.Shared._SW.GlobalWar;

namespace Content.Client._SW.GlobalWar.UI;

public sealed partial class SWGlobalWarMarketConsoleMenu
{
    private NetEntity[] _supplyArsenals = [];

    private void InitializeSupplyArsenals()
    {
        SupplyArsenalPicker.OnItemSelected += args =>
        {
            if (_supplyRefreshing || FlowDraft.PendingSupply != null)
                return;
            FlowDraft.SupplyArsenal = args.Id >= 0 && args.Id < _supplyArsenals.Length ? _supplyArsenals[args.Id] : null;
            CaptureSupplyArsenalDecision();
            RefreshSupply();
        };
        SupplyArsenalReload.OnPressed += _ =>
        {
            if (SupplyArsenalReload.Disabled)
                return;
            CaptureSupplyArsenalDecision();
            RefreshSupply();
        };
        SupplyApproveArsenal.OnPressed += _ =>
        {
            if (SupplyApproveArsenal.Disabled || _state?.Supply is not { CanPurchase: true } view ||
                FlowDraft.SupplyArsenal is not { } point ||
                FlowDraft.SupplyArsenalDecision is not { } decision ||
                view.Orders.FirstOrDefault(o => o.Id == decision.Order) is not { } order ||
                order.Outstanding != decision.Outstanding || order.ArsenalRevision != decision.Revision)
                return;
            SendSupply(new(_state.Generation, Guid.NewGuid(), _state.AssignmentRevision,
                SWGlobalWarSupplyAction.ApproveArsenal, order.Kind, decision.Revision, decision.Order,
                Recipient: point, Quantity: decision.Outstanding, HeadDecision: FlowDraft.SupplyHead));
        };
    }

    private void CaptureSupplyArsenalDecision()
    {
        var order = _state?.Supply?.Orders.FirstOrDefault(o => o.Id == FlowDraft.SupplyOrder);
        FlowDraft.SupplyArsenalDecision = FlowDraft.SupplyArsenal != null && order != null ?
            new(order.Id, order.Number, order.Outstanding, order.ArsenalRevision) : null;
    }

    private void RefreshSupplyArsenals(SWGlobalWarSupplyView view, SWGlobalWarSupplyOrderView? order, bool pending)
    {
        SetText(SupplyAllowedArsenals, order == null ? string.Empty : Loc.GetString("sw-global-war-supply-allowed-arsenals",
            ("points", string.Join(", ", order.AllowedArsenals ?? [])), ("count", order.Outstanding)));
        SupplyArsenalControls.Visible = view.CanPurchase && order is { Outstanding: > 0 };
        var points = view.Stores.Where(s => s.Arsenal).ToArray();
        _supplyArsenals = points.Select(s => s.Entity).ToArray();
        SupplyArsenalPicker.Clear();
        SupplyArsenalPicker.AddItem(Loc.GetString("sw-global-war-supply-select-recipient"), -1);
        for (var i = 0; i < points.Length; i++)
            SupplyArsenalPicker.AddItem(StoreLabel(points[i]), i);
        var selected = FlowDraft.SupplyArsenal is { } point ? Array.IndexOf(_supplyArsenals, point) : -1;
        SupplyArsenalPicker.SelectId(selected);
        SupplyArsenalPicker.Disabled = pending;
        var decision = FlowDraft.SupplyArsenalDecision;
        var changed = decision != null && order != null &&
            (decision.Order != order.Id || decision.Outstanding != order.Outstanding || decision.Revision != order.ArsenalRevision);
        SetText(SupplyArsenalChanged, !changed ? string.Empty : Loc.GetString("sw-global-war-supply-arsenal-changed",
            ("oldOrder", decision!.Number), ("old", decision.Outstanding), ("order", order!.Number), ("current", order.Outstanding)));
        SupplyArsenalReload.Visible = changed || decision == null && selected >= 0;
        SupplyArsenalReload.Disabled = pending || selected < 0 || order is not { Outstanding: > 0 };
        SupplyApproveArsenal.Disabled = pending || selected < 0 || order is not { Outstanding: > 0 } ||
            decision == null || changed || view.WarHeadDecisionRequired && !FlowDraft.SupplyHead;
    }
}

public sealed record SWGlobalWarSupplyArsenalDecision(Guid Order, int Number, int Outstanding, long Revision);
