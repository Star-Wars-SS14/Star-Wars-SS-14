using System.Numerics;
using Content.Client._SW.UserInterface;
using Content.Shared._SW.GlobalWar;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._SW.GlobalWar.UI;

/// <summary>
/// One good, in two shapes. Beside the map a short row with a bar, whose chosen one opens to twelve figures. Without the
/// map a line of a table with the same twelve figures. Values update in place, so the scroll position stays.
/// </summary>
internal sealed class SWGlobalWarMarketGoodRow : ContainerButton
{
    public const float NameWidth = 40;
    public const float StockWidth = 126;
    public const float ColumnWidth = 66;

    /// <summary>Room at the right of the table for the scroll bar, which otherwise covered the last figure.</summary>
    private const float TableEnd = 12;

    private static readonly string[] Keys =
    {
        "available", "reserved", "production", "recipe-use", "growth-use", "storage-space",
        "consumption", "demand", "supply", "incoming", "uncovered", "coverage",
    };

    /// <summary>Columns a narrow table leaves out: they matter least when choosing what to move.</summary>
    private static readonly int[] NarrowHidden = { 3, 4, 5, 9 };

    /// <summary>The order of the two columns of an opened row, as pairs of indexes into <see cref="Keys"/>.</summary>
    private static readonly (int Left, int Right)[] OpenedPairs = { (0, 1), (2, 6), (3, 4), (7, 8), (9, 10), (11, 5) };

    private readonly BoxContainer _short;
    private readonly BoxContainer _opened;
    private readonly BoxContainer _table;
    private readonly Label _shortStock = new() { FontOverride = SWGlobalWarConsoleStyle.Strong, FontColorOverride = Color.White };
    private readonly Label _tableStock = new() { FontOverride = SWGlobalWarConsoleStyle.Small, FontColorOverride = Color.White };
    private readonly SWGlobalWarMarketBar _shortBar = new() { MinHeight = 5, MaxHeight = 5 };
    private readonly SWGlobalWarMarketBar _tableBar = new() { MinHeight = 5, MaxHeight = 5 };
    // In the tooltip, because the narrow panel can cut a long name.
    private readonly string _name;
    private readonly Label[] _shortValues = new Label[Keys.Length];
    private readonly Label[] _tableValues = new Label[Keys.Length];
    private bool _open;
    private bool _tableShape;

    public SWGlobalWarMarketGoodRow(SWGlobalWarCommodityPrototype commodity)
    {
        HorizontalExpand = true;
        ModulateSelfOverride = Color.White;
        MinHeight = 36;

        var name = Loc.GetString(commodity.Name);
        _name = name;
        var layers = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, Margin = new Thickness(6, 2), SeparationOverride = 4 };

        _short = new BoxContainer { SeparationOverride = 8, MinHeight = 40 };
        _short.AddChild(Picture(commodity));
        var shortBody = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, VerticalAlignment = VAlignment.Center, SeparationOverride = 4 };
        var words = new BoxContainer { SeparationOverride = 8 };
        // The panel beside the map is narrow: the longest names take two lines.
        words.AddChild(new Label { Text = TwoLines(name), HorizontalExpand = true, ClipText = true, FontOverride = SWGlobalWarConsoleStyle.Strong, FontColorOverride = Color.White });
        words.AddChild(_shortStock);
        shortBody.AddChild(words);
        shortBody.AddChild(_shortBar);
        _short.AddChild(shortBody);
        layers.AddChild(_short);

        _opened = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 0, Visible = false, Margin = new Thickness(4, 0, 4, 4) };
        foreach (var (left, right) in OpenedPairs)
        {
            var line = new BoxContainer { SeparationOverride = 16 };
            line.AddChild(Pair(left));
            line.AddChild(Pair(right));
            _opened.AddChild(line);
        }
        layers.AddChild(_opened);

        _table = new BoxContainer { SeparationOverride = 0, Visible = false, MinHeight = 32, Margin = new Thickness(0, 0, TableEnd, 0) };
        _table.AddChild(Picture(commodity));
        _table.AddChild(new Label
        {
            Text = name,
            HorizontalExpand = true,
            ClipText = true,
            Margin = new Thickness(8, 0, 0, 0),
            VAlign = Label.VAlignMode.Center,
            FontOverride = SWGlobalWarConsoleStyle.Strong,
            FontColorOverride = Color.White,
        });
        var stock = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SetWidth = StockWidth, VerticalAlignment = VAlignment.Center, SeparationOverride = 2 };
        stock.AddChild(_tableStock);
        _tableBar.Margin = new Thickness(0, 0, 14, 0);
        stock.AddChild(_tableBar);
        _table.AddChild(stock);
        for (var i = 0; i < Keys.Length; i++)
        {
            _tableValues[i] = new Label { SetWidth = ColumnWidth, Align = Label.AlignMode.Right, VAlign = Label.VAlignMode.Center, ClipText = true, FontOverride = SWGlobalWarConsoleStyle.Small };
            _table.AddChild(_tableValues[i]);
        }
        layers.AddChild(_table);

        AddChild(layers);
        Refresh();
    }

    /// <summary>The caption of the table: the same columns as <see cref="_table"/>.</summary>
    public static Control Header()
    {
        var header = new BoxContainer { SeparationOverride = 0, MinHeight = 26, Margin = new Thickness(6, 0, 6 + TableEnd, 0), HorizontalExpand = true };
        var good = HeaderLabel("sw-global-war-market-col-good", null, true, Label.AlignMode.Left);
        good.Margin = new Thickness(48, 0, 0, 0);
        header.AddChild(good);
        header.AddChild(HeaderLabel("sw-global-war-market-col-stock", StockWidth, false, Label.AlignMode.Left));
        foreach (var key in Keys)
        {
            var label = HeaderLabel("sw-global-war-market-col-" + key, ColumnWidth, false);
            label.Name = key;
            header.AddChild(label);
        }
        return header;
    }

    /// <summary>Hides the columns a narrow table leaves out, in the caption made by <see cref="Header"/>.</summary>
    public static void SetHeaderNarrow(Control header, bool narrow)
    {
        foreach (var index in NarrowHidden)
        {
            foreach (var child in header.Children)
            {
                if (child.Name == Keys[index])
                    child.Visible = !narrow;
            }
        }
    }

    public void SetNarrow(bool narrow)
    {
        foreach (var index in NarrowHidden)
            _tableValues[index].Visible = !narrow;
    }

    private static Label HeaderLabel(string key, float? width, bool expand, Label.AlignMode align = Label.AlignMode.Right)
    {
        var label = new Label
        {
            Text = Loc.GetString(key),
            Align = align,
            VAlign = Label.VAlignMode.Center,
            ClipText = true,
            HorizontalExpand = expand,
            FontOverride = SWGlobalWarConsoleStyle.Small,
            FontColorOverride = SWGlobalWarConsoleStyle.TextDim,
        };
        if (width is { } set)
        {
            // Captions are right-aligned like their figures: a small inset keeps two long words from running together.
            label.SetWidth = set - 4;
            label.Margin = new Thickness(4, 0, 0, 0);
        }
        return label;
    }

    private static Control Picture(SWGlobalWarCommodityPrototype commodity)
    {
        return SWGlobalWarMarketPresentation.CommodityPicture(commodity, new Vector2(40, 44), new Thickness(0, -5));
    }

    private Control Pair(int index)
    {
        var line = new BoxContainer { SeparationOverride = 6, HorizontalExpand = true };
        if (MetricIcon(Keys[index]) is { } metric)
            line.AddChild(SWGlobalWarMarketPresentation.SmallIcon("metrics", metric));
        line.AddChild(new Label
        {
            Text = Loc.GetString("sw-global-war-market-open-" + Keys[index]),
            HorizontalExpand = true,
            ClipText = true,
            FontOverride = SWGlobalWarConsoleStyle.Small,
            FontColorOverride = SWGlobalWarConsoleStyle.TextDim,
        });
        _shortValues[index] = new Label { FontOverride = SWGlobalWarConsoleStyle.Small };
        line.AddChild(_shortValues[index]);
        return line;
    }

    private static string? MetricIcon(string key) => key switch
    {
        "reserved" => "reserved",
        "production" => "throughput",
        "incoming" => "in_transit",
        _ => null,
    };

    public void SetShape(bool table)
    {
        if (_tableShape == table)
            return;
        _tableShape = table;
        Refresh();
    }

    public void SetOpen(bool open)
    {
        if (_open == open)
            return;
        _open = open;
        Refresh();
    }

    private void Refresh()
    {
        _table.Visible = _tableShape;
        _short.Visible = !_tableShape;
        _opened.Visible = !_tableShape && _open;
        StyleBoxOverride = SWGlobalWarConsoleStyle.RowBox(_open);
    }

    public void Update(SWGlobalWarMarketTotals totals)
    {
        var stock = $"{SWGlobalWarMarketPresentation.Amount(totals.Stock)} / {SWGlobalWarMarketPresentation.Amount(totals.Target)}";
        _shortStock.Text = _tableStock.Text = stock;
        var bar = totals.Target > 0 ? totals.Available : 1;
        var maximum = totals.Target > 0 ? totals.Target : 1;
        var fill = totals.Demand > 0 ? SWGlobalWarConsoleStyle.Bad : SWGlobalWarConsoleStyle.Good;
        foreach (var part in new[] { _shortBar, _tableBar })
        {
            part.Value = bar;
            part.Maximum = maximum;
            part.Fill = fill;
        }

        var space = Math.Max(0, totals.Capacity - totals.Stock);
        Set(0, totals.Available);
        Set(1, totals.Reserved);
        Set(2, totals.Production);
        Set(3, totals.RecipeUse);
        Set(4, totals.GrowthUse);
        Set(5, space);
        Set(6, totals.Consumption);
        Set(7, totals.Demand);
        Set(8, totals.Supply);
        Set(9, totals.Incoming);
        Set(10, totals.Uncovered, totals.Uncovered > 0 ? SWGlobalWarConsoleStyle.Bad : null);

        var low = totals.Coverage is < LowCoverageMinutes && totals.Demand > 0;
        var color = low ? SWGlobalWarConsoleStyle.Bad : totals.Coverage == null ? SWGlobalWarConsoleStyle.TextDim : Color.White;
        Show(11, totals.Coverage is { } minutes ? minutes.ToString("0.##") : Loc.GetString("sw-global-war-market-coverage-none"),
            SWGlobalWarMarketPresentation.Coverage(totals.Coverage), color);

        var tip = _name + "\n" + Loc.GetString("sw-global-war-market-target-hint") + "\n" + Loc.GetString("sw-global-war-market-storage-values",
            ("stock", totals.Stock.ToString("0.###")), ("capacity", totals.Capacity.ToString("0.###")),
            ("incoming", (totals.Incoming + totals.ReservedIncoming).ToString("0.###")));
        if (ToolTip != tip)
            ToolTip = tip;
    }

    private const double LowCoverageMinutes = 10;
    private const int LongName = 14;

    private static string TwoLines(string name)
    {
        var split = name.LastIndexOf(' ');
        return name.Length >= LongName && split > 0 ? name[..split] + '\n' + name[(split + 1)..] : name;
    }

    private void Set(int index, double value, Color? color = null)
    {
        var text = SWGlobalWarMarketPresentation.Amount(value);
        Show(index, text, text, color ?? (value == 0 ? SWTerminalStyle.TextOff : Color.White));
    }

    private void Show(int index, string table, string opened, Color color)
    {
        if (_tableValues[index].Text != table)
            _tableValues[index].Text = table;
        if (_shortValues[index].Text != opened)
            _shortValues[index].Text = opened;
        _tableValues[index].FontColorOverride = color;
        _shortValues[index].FontColorOverride = color == SWTerminalStyle.TextOff ? SWGlobalWarConsoleStyle.TextDim : color;
    }
}
