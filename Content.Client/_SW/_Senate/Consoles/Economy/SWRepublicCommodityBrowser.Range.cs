using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared._SW._Senate.Economy;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._SW._Senate.Consoles.Economy;

public sealed partial class SWRepublicCommodityBrowser
{
    private enum EndpointPick : byte { None, Start, End }
    private EndpointPick _pickingEndpoint;
    private readonly Dictionary<string, string> _rangeRecipes = new();
    private Dictionary<string, string> ActiveRecipes => IsRangeView ? _rangeRecipes : _selectedRecipes;
    private readonly Button _rangeButton = SWConsoleStyle.TabButton("Участок");
    private readonly Button _rangeStart = SWConsoleStyle.TabButton("Начало");
    private readonly Button _rangeEnd = SWConsoleStyle.TabButton("Конец");
    private readonly RichTextLabel _rangeStartLabel = SWConsoleStyle.Wrap("Начало", SWConsoleStyle.TextBright, 260);
    private readonly RichTextLabel _rangeEndLabel = SWConsoleStyle.Wrap("Конец", SWConsoleStyle.TextBright, 260);
    private readonly WrapContainer _rangeControls = Wrap();

    public string? RangeStartId { get; private set; }
    public string? RangeEndId { get; private set; }
    public bool IsRangeView => RangeStartId != null && RangeEndId != null && _pickingEndpoint == EndpointPick.None;

    private void InitializeRangeControls()
    {
        _rangeButton.Name = "ProductionRange";
        _rangeControls.Name = "ProductionRangeControls";
        _rangeControls.Visible = false;
        _rangeStart.Name = "ProductionRangeStart";
        _rangeEnd.Name = "ProductionRangeEnd";
        foreach (var button in new[] { _rangeStart, _rangeEnd })
        {
            button.MinWidth = 260;
            button.MaxWidth = 300;
            button.Label.Visible = false;
            _rangeControls.AddChild(button);
        }
        _rangeStart.AddChild(_rangeStartLabel);
        _rangeEnd.AddChild(_rangeEndLabel);
        _rangeStart.OnPressed += _ => BeginRangePick(true);
        _rangeEnd.OnPressed += _ => BeginRangePick(false);
        var clear = SWConsoleStyle.TabButton("Снять границы");
        clear.Name = "ProductionRangeClear";
        clear.OnPressed += _ => ClearRange();
        _rangeControls.AddChild(clear);
        AddChild(_rangeControls);
    }

    public void BeginRangePick(bool start)
    {
        var wasRange = IsRangeView;
        _pickingEndpoint = start ? EndpointPick.Start : EndpointPick.End;
        RefreshRecipes();
        if (wasRange) RefreshGraph();
        RefreshRangeControls();
        RefreshStatus();
    }

    public bool SelectRangeStart(string id) => SetRangeEndpoint(id, true);
    public bool SelectRangeEnd(string id) => SetRangeEndpoint(id, false);

    private bool SetRangeEndpoint(string id, bool start)
    {
        if (NodeTitle(id) == null) return false;
        if (start) RangeStartId = id;
        else RangeEndId = id;
        _pickingEndpoint = EndpointPick.None;
        RefreshRecipes();
        if (IsRangeView)
        {
            RefreshGraph();
            Graph.SelectNode(id);
            _selectedNode = Graph.Graph!.Nodes.FirstOrDefault(n => n.Id == id);
            RefreshDetail();
        }
        else
        {
            Graph.SetEndpoints(RangeStartId, RangeEndId);
            RefreshRangeControls();
            RefreshStatus();
        }
        return true;
    }

    public void ClearRange()
    {
        ResetRangeSelection();
        RefreshRecipes();
        RefreshGraph();
    }

    private void ResetRangeSelection()
    {
        RangeStartId = RangeEndId = null;
        _rangeRecipes.Clear();
        _pickingEndpoint = EndpointPick.None;
    }

    // Endpoint choices apply only while that exact manufacturing node remains a boundary.
    // Keeping them separate prevents a removed boundary from silently hiding other paths.
    private Dictionary<string, string> RangeRecipesWithEndpoints()
    {
        var choices = new Dictionary<string, string>(_rangeRecipes);
        foreach (var id in new[] { RangeStartId, RangeEndId })
        {
            if (id == null || !id.StartsWith("recipe:", StringComparison.Ordinal)) continue;
            foreach (var output in _prototypes.Index<SWProductionRecipePrototype>(id[7..]).Outputs.Keys)
                choices[output.Id] = id[7..];
        }
        return choices;
    }

    private string? EndpointRecipe(string commodity)
    {
        foreach (var id in new[] { RangeEndId, RangeStartId })
            if (id != null && id.StartsWith("recipe:", StringComparison.Ordinal) &&
                _prototypes.Index<SWProductionRecipePrototype>(id[7..]).Outputs.ContainsKey(commodity)) return id[7..];
        return null;
    }

    private void SelectGraphNode(SWProductionNode node)
    {
        _selectedNode = node;
        var picking = _pickingEndpoint;
        if (picking == EndpointPick.Start)
        {
            SelectRangeStart(node.Id);
            if (RangeEndId == null) BeginRangePick(false);
        }
        else if (picking == EndpointPick.End) SelectRangeEnd(node.Id);
        RefreshDetail();
    }

    private void RefreshRangeControls()
    {
        _rangeControls.Visible = RangeStartId != null || RangeEndId != null || _pickingEndpoint != EndpointPick.None;
        _rangeStart.Text = "Начало" + (RangeStartId is { } from ? ": " + NodeTitle(from) : "");
        _rangeEnd.Text = "Конец" + (RangeEndId is { } to ? ": " + NodeTitle(to) : "");
        _rangeStart.ToolTip = _rangeStart.Text;
        _rangeEnd.ToolTip = _rangeEnd.Text;
        SetRangeLabel(_rangeStartLabel, _rangeStart.Text!);
        SetRangeLabel(_rangeEndLabel, _rangeEnd.Text!);
        SWConsoleStyle.StyleTabButton(_rangeStart, _pickingEndpoint == EndpointPick.Start);
        SWConsoleStyle.StyleTabButton(_rangeEnd, _pickingEndpoint == EndpointPick.End);
        SWConsoleStyle.StyleTabButton(_rangeButton, _rangeControls.Visible);
    }

    private static void SetRangeLabel(RichTextLabel label, string text)
    {
        var message = new FormattedMessage();
        message.AddText(text);
        label.SetMessage(message);
    }

    private void RefreshStatus()
    {
        _status.Text = _pickingEndpoint switch
        {
            EndpointPick.Start => "Выберите начало на схеме",
            EndpointPick.End => "Выберите конец на схеме",
            _ when IsRangeView && RangeStartId != RangeEndId && Graph.Graph?.Edges.Count == 0 => "Нет производственного пути",
            _ => _economy is not { Ready: true } ? "Каталог производства" : _economy.Paused
                ? "Экономика приостановлена" : $"Экономика: {Number(_economy.Hour)} ч",
        };
    }

    private string? NodeTitle(string id)
    {
        var split = id.IndexOf(':');
        if (split < 0 || split + 1 >= id.Length) return null;
        var reference = id[(split + 1)..];
        return id[..split] switch
        {
            "good" when _prototypes.TryIndex<SWCommodityPrototype>(reference, out var good) => good.Name,
            "recipe" when _prototypes.TryIndex<SWProductionRecipePrototype>(reference, out var recipe) => recipe.Name,
            "demand" when _prototypes.TryIndex<SWCommodityDemandPrototype>(reference, out var demand) => demand.Name,
            "condition" when _prototypes.TryIndex<SWProductionConditionPrototype>(reference, out var condition) => condition.Name,
            _ => null,
        };
    }
}
