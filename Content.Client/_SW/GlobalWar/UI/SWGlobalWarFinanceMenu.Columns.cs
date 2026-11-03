using System.Linq;
using Content.Client._SW.UserInterface;
using Content.Shared._SW.GlobalWar.Politics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._SW.GlobalWar.UI;

/// <summary>The budget as the ministries think of it: the treasury on top and a column of its own for each budget.</summary>
public sealed partial class SWGlobalWarFinanceMenu
{
    private static readonly Color TreasuryColor = Color.FromHex("#F2D27A");
    private static readonly Color ReservedColor = Color.FromHex("#8A91A0");
    private readonly Dictionary<SWGlobalWarAccount, BudgetColumn> _columns = new();

    private static Color AccountColor(SWGlobalWarAccount account) => account switch
    {
        SWGlobalWarAccount.War => Color.FromHex("#E0555A"),
        SWGlobalWarAccount.Logistics => Color.FromHex("#4FB6C8"),
        SWGlobalWarAccount.Industry => Color.FromHex("#E08A3C"),
        _ => TreasuryColor,
    };

    /// <summary>Under this width, which is 1280x720 at the automatic UI scale of 1.36, the columns stack their parts.</summary>
    private const float NarrowWidth = 1100;

    private void FitWidth()
    {
        var narrow = Size.X < NarrowWidth;
        foreach (var column in _columns.Values)
            column.SetNarrow(narrow);
        TreasuryPlate.SetWidth = narrow ? 350 : 400;
        TreasuryRow.Margin = new Thickness(narrow ? 100 : 170, 8, 34, 8);
        ShareLegend.SeparationOverride = narrow ? 8 : 14;
    }

    private string Money(long sum) => Loc.GetString("sw-global-war-finance-sum", ("sum", sum));

    private void ShowBalances(IReadOnlyCollection<SWGlobalWarBudgetView> budgets)
    {
        BalancesNote.Visible = budgets.Count == 0;
        Plain(BalancesNote, budgets.Count == 0 ? Loc.GetString("sw-global-war-finance-own-orders") : string.Empty);
        ShowTreasury(budgets);

        var ministries = budgets.Where(b => b.Account != SWGlobalWarAccount.Treasury).ToList();
        if (!_columns.Keys.SequenceEqual(ministries.Select(b => b.Account)))
        {
            OrderCreation.Orphan();
            Columns.RemoveAllChildren();
            _columns.Clear();
            foreach (var budget in ministries)
            {
                var column = new BudgetColumn(this, budget.Account);
                _columns.Add(budget.Account, column);
                column.SetNarrow(Size.X < NarrowWidth);
                Columns.AddChild(column.Root);
            }

            TreasurySlot.AddChild(OrderCreation);
            OrderCreation.Visible = false;
            _orderFormOpen = false;
        }

        Columns.Visible = ministries.Count != 0;
        foreach (var budget in ministries)
            _columns[budget.Account].Update(budget);
    }

    private void ShowTreasury(IReadOnlyCollection<SWGlobalWarBudgetView> budgets)
    {
        var treasury = budgets.FirstOrDefault(b => b.Account == SWGlobalWarAccount.Treasury);
        TreasuryRow.Visible = budgets.Count != 0;
        if (treasury != null)
        {
            TreasuryFree.Text = Money(treasury.Balance - treasury.Reserved);
            TreasuryTotals.Text = Loc.GetString("sw-global-war-finance-totals", ("balance", treasury.Balance), ("reserved", treasury.Reserved));
        }

        TreasuryWord.Parent!.Parent!.Visible = treasury != null;
        var total = budgets.Sum(b => b.Balance);
        ShareWord.Text = Loc.GetString("sw-global-war-finance-state-money", ("sum", total));
        Shares.Set(budgets.Select(b => (AccountColor(b.Account), (double) b.Balance)).ToArray());
        ShareLegend.RemoveAllChildren();
        foreach (var budget in budgets)
        {
            var part = new BoxContainer { SeparationOverride = 4 };
            part.AddChild(new PanelContainer { SetSize = new System.Numerics.Vector2(10, 10), VerticalAlignment = VAlignment.Center, PanelOverride = SWTerminalStyle.Fill(AccountColor(budget.Account)) });
            part.AddChild(new Label
            {
                Text = Loc.GetString("sw-global-war-finance-share", ("name", ShortName(budget.Account)), ("percent", total > 0 ? budget.Balance * 100 / total : 0)),
                FontOverride = SWGlobalWarConsoleStyle.Small,
                FontColorOverride = SWGlobalWarConsoleStyle.TextBright,
            });
            ShareLegend.AddChild(part);
        }
    }

    private static string ShortName(SWGlobalWarAccount account) => Loc.GetString("sw-global-war-finance-short-" + account.ToString().ToLowerInvariant());

    /// <summary>Puts the one order form into the column it is for, or closes it when pressed again.</summary>
    private void ToggleOrderForm(SWGlobalWarAccount account, BoxContainer slot)
    {
        var open = !(_orderFormOpen && _orderAccount == account);
        OrderCreation.Orphan();
        slot.AddChild(OrderCreation);
        OrderCreation.Visible = open;
        slot.Visible = open || slot != TreasurySlot;
        _orderFormOpen = open;
        _orderAccount = account;
        RefreshEmployees();
        RefreshControls();
    }

    /// <summary>Every order stands in its own account's column, with its own button.</summary>
    private void ShowOrders()
    {
        foreach (var column in _columns.Values)
            column.Orders.RemoveAllChildren();
        TreasuryOrders.RemoveAllChildren();
        foreach (var child in MyOrders.Children.Where(c => c != OrdersTitle).ToArray())
            child.Orphan();

        foreach (var order in _orders)
        {
            var row = OrderRow(order);
            if (_columns.TryGetValue(order.Account, out var column))
                column.Orders.AddChild(row);
            else if (_view?.Budgets.Count != 0 && order.Account == SWGlobalWarAccount.Treasury)
                TreasuryOrders.AddChild(row);
            else
                MyOrders.AddChild(row);
        }

        TreasuryOrders.Visible = TreasuryOrders.ChildCount != 0;
        MyOrders.Visible = MyOrders.ChildCount > 1;
        foreach (var column in _columns.Values)
            column.ShowCount();
    }

    private PanelContainer OrderRow(SWGlobalWarOrderView order)
    {
        var words = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, VerticalAlignment = VAlignment.Center };
        words.AddChild(new Label { Text = order.Purpose, ClipText = true, ToolTip = order.Purpose, MouseFilter = MouseFilterMode.Stop, FontOverride = SWGlobalWarConsoleStyle.Strong, FontColorOverride = Color.White });
        words.AddChild(new Label
        {
            // The time first: a long name may be cut, and it is in the tooltip anyway.
            Text = Loc.GetString("sw-global-war-finance-order-who", ("employee", order.Employee), ("time", order.Expires is { } expires ? Clock(expires - _view!.Time) : "—")),
            ToolTip = order.Employee,
            MouseFilter = MouseFilterMode.Stop,
            ClipText = true,
            FontOverride = SWGlobalWarConsoleStyle.Small,
            FontColorOverride = SWGlobalWarConsoleStyle.TextDim,
        });
        var side = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 2, VerticalAlignment = VAlignment.Center };
        side.AddChild(new Label { Text = Money(order.Remaining), Align = Label.AlignMode.Right, FontOverride = SWGlobalWarConsoleStyle.Strong, FontColorOverride = SWGlobalWarConsoleStyle.Money });
        if (order.CanRelease)
        {
            var release = new SWGlobalWarConsoleButton { Text = Loc.GetString("sw-global-war-finance-release-short"), Disabled = _pending != null, ToolTip = Loc.GetString("sw-global-war-finance-release") };
            release.OnPressed += _ => Request(SWGlobalWarFinanceAction.Release, order);
            side.AddChild(release);
        }

        var line = new BoxContainer { SeparationOverride = 8, Margin = new Thickness(8, 4) };
        line.AddChild(new PanelContainer { SetWidth = 3, PanelOverride = SWTerminalStyle.Fill(AccountColor(order.Account)) });
        line.AddChild(words);
        line.AddChild(side);
        var row = new PanelContainer { HorizontalExpand = true, PanelOverride = SWGlobalWarConsoleStyle.RowBox(false) };
        row.AddChild(line);
        return row;
    }

    private void ShowJournal(SWGlobalWarFinancePage page)
    {
        Journal.RemoveAllChildren();
        if (!page.Allowed || page.Entries.Count == 0)
        {
            Journal.AddChild(new Label
            {
                Text = Loc.GetString(page.Allowed ? "sw-global-war-government-none" : "sw-global-war-finance-page-expired"),
                FontOverride = SWGlobalWarConsoleStyle.Small,
                FontColorOverride = SWGlobalWarConsoleStyle.TextDim,
            });
            return;
        }

        foreach (var entry in page.Entries)
            Journal.AddChild(JournalRow(entry));
    }

    private PanelContainer JournalRow(SWGlobalWarFinanceEntryView entry)
    {
        var line = new BoxContainer { SeparationOverride = 8, Margin = new Thickness(8, 2) };
        line.AddChild(new Label { Text = Clock(entry.At), VAlign = Label.VAlignMode.Center, FontOverride = SWGlobalWarConsoleStyle.Small, FontColorOverride = SWGlobalWarConsoleStyle.TextDim });
        line.AddChild(new PanelContainer { SetSize = new System.Numerics.Vector2(10, 10), VerticalAlignment = VAlignment.Center, PanelOverride = SWTerminalStyle.Fill(AccountColor(entry.Account)) });
        if (entry.Destination is { } target)
            line.AddChild(new PanelContainer { SetSize = new System.Numerics.Vector2(10, 10), VerticalAlignment = VAlignment.Center, PanelOverride = SWTerminalStyle.Fill(AccountColor(target)) });
        var what = Loc.GetString(entry.Action) + (entry.Kind == SWGlobalWarFinanceEntryKind.Ordinary ? string.Empty : ". " + Loc.GetString("sw-global-war-finance-kind-" + entry.Kind.ToString().ToLowerInvariant()));
        line.AddChild(new Label { Text = what, HorizontalExpand = true, ClipText = true, VAlign = Label.VAlignMode.Center, FontOverride = SWGlobalWarConsoleStyle.Small, FontColorOverride = SWGlobalWarConsoleStyle.TextBright });
        var author = entry.AuthorRole is { } role ? Loc.GetString("sw-global-war-finance-author", ("name", entry.AuthorName),
            ("role", Loc.GetString("sw-global-war-role-" + role.ToString().ToLowerInvariant()))) : Loc.GetString("sw-global-war-finance-system");
        line.AddChild(new Label { Text = author, SetWidth = 300, ClipText = true, VAlign = Label.VAlignMode.Center, FontOverride = SWGlobalWarConsoleStyle.Small, FontColorOverride = SWGlobalWarConsoleStyle.TextDim });
        line.AddChild(new Label { Text = Money(entry.Amount), MinWidth = 110, Align = Label.AlignMode.Right, VAlign = Label.VAlignMode.Center, FontOverride = SWGlobalWarConsoleStyle.SmallStrong, FontColorOverride = SWGlobalWarConsoleStyle.Money });
        var row = new PanelContainer { HorizontalExpand = true, PanelOverride = SWGlobalWarConsoleStyle.RowBox(false), MouseFilter = MouseFilterMode.Stop };
        var details = author;
        if (entry.Destination is { } destination)
            details += "\n" + Loc.GetString("sw-global-war-finance-entry-destination", ("account", AccountName(destination)));
        if (!string.IsNullOrEmpty(entry.Purpose))
            details += "\n" + entry.Purpose;
        row.ToolTip = details;
        row.AddChild(line);
        return row;
    }

    /// <summary>One budget: its colour and scene, what is free, its transfers and its orders.</summary>
    private sealed class BudgetColumn
    {
        public readonly PanelContainer Root;
        public readonly BoxContainer Orders = new() { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 4 };
        public readonly BoxContainer Slot = new() { Orientation = BoxContainer.LayoutOrientation.Vertical };
        public readonly SWGlobalWarConsoleButton Here = new() { Text = Loc.GetString("sw-global-war-finance-transfer-here"), HorizontalExpand = true, MinHeight = 30 };
        public readonly SWGlobalWarConsoleButton Back = new() { Text = Loc.GetString("sw-global-war-finance-transfer-back"), HorizontalExpand = true, MinHeight = 30 };
        public readonly BoxContainer TransferRow = new() { SeparationOverride = 6 };
        private readonly SWGlobalWarFinanceMenu _menu;
        private readonly SWGlobalWarAccount _account;
        private readonly Label _free = new() { FontOverride = SWGlobalWarConsoleStyle.Bold(20), FontColorOverride = SWGlobalWarConsoleStyle.Money };
        private readonly SWGlobalWarShareBar _bar = new() { SetHeight = 8 };
        private readonly Label _total = new() { HorizontalExpand = true, FontOverride = SWGlobalWarConsoleStyle.Small, FontColorOverride = SWGlobalWarConsoleStyle.TextBright };
        private readonly Label _reserved = new() { FontOverride = SWGlobalWarConsoleStyle.Small };
        private readonly Label _count = new() { HorizontalExpand = true, Align = Label.AlignMode.Right, FontOverride = SWGlobalWarConsoleStyle.Small, FontColorOverride = SWGlobalWarConsoleStyle.TextDim };
        private readonly BoxContainer _totals = new() { SeparationOverride = 8 };
        private readonly SWGlobalWarConsoleButton _newOrder = new() { Text = Loc.GetString("sw-global-war-finance-new-order-plus"), MinHeight = 30 };

        public BudgetColumn(SWGlobalWarFinanceMenu menu, SWGlobalWarAccount account)
        {
            _menu = menu;
            _account = account;
            Here.SetAppearance(SWGlobalWarConsoleStyle.TextBright, money: true);
            Here.OnPressed += _ => menu.RequestTransfer(menu._account, account);
            Back.OnPressed += _ => menu.RequestTransfer(account, SWGlobalWarAccount.Treasury);
            _newOrder.OnPressed += _ => menu.ToggleOrderForm(account, Slot);
            TransferRow.AddChild(Here);
            TransferRow.AddChild(Back);

            var scene = new SWGlobalWarBand { SetHeight = 96, Scene = SWGlobalWarConsoleStyle.Picture($"budget_{account.ToString().ToLowerInvariant()}_{SWGlobalWarConsoleStyle.SideLetter()}") };
            var plate = new SWGlobalWarPlate { HorizontalAlignment = HAlignment.Left, VerticalAlignment = VAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            var words = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, Margin = new Thickness(10, 6, 18, 6) };
            words.AddChild(new Label { Text = AccountName(account), FontOverride = SWGlobalWarConsoleStyle.Strong, FontColorOverride = Color.White });
            words.AddChild(new Label { Text = Loc.GetString("sw-global-war-finance-about-" + account.ToString().ToLowerInvariant()), FontOverride = SWGlobalWarConsoleStyle.Small, FontColorOverride = SWGlobalWarConsoleStyle.TextDim });
            words.AddChild(_free);
            plate.AddChild(words);
            scene.AddChild(plate);

            _totals.AddChild(_total);
            _totals.AddChild(_reserved);
            var ordersHead = new BoxContainer();
            ordersHead.AddChild(new Label { Text = Loc.GetString("sw-global-war-finance-orders"), FontOverride = SWGlobalWarConsoleStyle.Strong, FontColorOverride = SWGlobalWarConsoleStyle.TextBright });
            ordersHead.AddChild(_count);

            var body = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 6, Margin = new Thickness(10, 8), VerticalExpand = true };
            body.AddChild(_bar);
            body.AddChild(_totals);
            body.AddChild(TransferRow);
            body.AddChild(ordersHead);
            body.AddChild(Orders);
            body.AddChild(Slot);
            body.AddChild(new Control { VerticalExpand = true });
            body.AddChild(_newOrder);

            var line = new PanelContainer { MinHeight = 3, PanelOverride = SWTerminalStyle.Fill(AccountColor(account)) };
            var stack = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 0 };
            stack.AddChild(line);
            stack.AddChild(scene);
            stack.AddChild(body);
            Root = new PanelContainer { HorizontalExpand = true, VerticalExpand = true, PanelOverride = SWTerminalStyle.Box(Color.FromHex("#101216"), AccountColor(account).WithAlpha(.6f), 1) };
            Root.AddChild(stack);
        }

        /// <summary>A third of a narrow window has no room for two figures or two buttons side by side.</summary>
        public void SetNarrow(bool narrow)
        {
            var orientation = narrow ? BoxContainer.LayoutOrientation.Vertical : BoxContainer.LayoutOrientation.Horizontal;
            _totals.Orientation = orientation;
            TransferRow.Orientation = orientation;
        }

        public void Update(SWGlobalWarBudgetView budget)
        {
            var free = budget.Balance - budget.Reserved;
            _free.Text = _menu.Money(free);
            _bar.Set((AccountColor(_account), free), (ReservedColor, budget.Reserved));
            _total.Text = Loc.GetString("sw-global-war-finance-total-short", ("sum", budget.Balance));
            _reserved.Text = Loc.GetString("sw-global-war-finance-reserved-short", ("sum", budget.Reserved));
            _reserved.FontColorOverride = budget.Reserved > 0 ? SWGlobalWarConsoleStyle.TextBright : SWTerminalStyle.TextOff;
            _newOrder.Visible = budget.CanManage;
            Root.Modulate = budget.CanManage || _menu._view?.CanTransfer == true ? Color.White : Color.White.WithAlpha(.75f);
            TransferRow.Visible = _menu._view?.CanTransfer == true;
        }

        public void ShowCount() => _count.Text = Orders.ChildCount.ToString();
    }
}
