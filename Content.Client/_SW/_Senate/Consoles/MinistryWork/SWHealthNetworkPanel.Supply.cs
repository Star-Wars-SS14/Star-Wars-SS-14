using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared._SW._Senate.Ministries;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._SW._Senate.Consoles.MinistryWork;

public sealed partial class SWHealthNetworkPanel
{
    private sealed record Supply(string Id, string Title, string Status, bool Completed, float Progress, string Detail, List<SWHealthMaterialView> Materials, SWHealthMaterialView? Payload = null);
    private readonly BoxContainer _supplyRows = Stack(), _supplyDetails = Stack();
    private readonly BoxContainer _supplyColumns = new() { HorizontalExpand = true, VerticalExpand = true, SeparationOverride = 10 };
    private readonly Dictionary<string, Button> _supplyButtons = new();
    private ScrollContainer _supplyList = default!;
    private Button _currentSupplies = default!, _finishedSupplies = default!;
    private string _selectedSupply = "";
    private bool _showCompleted;
    public string SelectedSupplyId => _selectedSupply;

    private Control BuildSupplyWorkspace()
    {
        var page = Stack();
        page.VerticalExpand = true;
        var tabs = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 4 };
        _currentSupplies = SWConsoleStyle.TabButton("В работе");
        _finishedSupplies = SWConsoleStyle.TabButton("Завершено");
        _currentSupplies.Name = "HealthSupplyCurrent";
        _finishedSupplies.Name = "HealthSupplyFinished";
        _currentSupplies.OnPressed += _ => { _showCompleted = false; RefreshSupply(); };
        _finishedSupplies.OnPressed += _ => { _showCompleted = true; RefreshSupply(); };
        tabs.AddChild(_currentSupplies); tabs.AddChild(_finishedSupplies);
        page.AddChild(tabs);
        _supplyList = Scroll(_supplyRows, "HealthSupplyList");
        _supplyColumns.AddChild(_supplyList);
        _supplyColumns.AddChild(Scroll(_supplyDetails, "HealthSupplyDetailScroll"));
        page.AddChild(_supplyColumns);
        return page;
    }
    protected override void Resized()
    {
        base.Resized();
        var wide = Width >= 850;
        _supplyColumns.Orientation = wide ? LayoutOrientation.Horizontal : LayoutOrientation.Vertical;
        if (_supplyList == null) return;
        _supplyList.MinWidth = wide ? 240 : 0;
        _supplyList.MaxWidth = wide ? 280 : float.PositiveInfinity;
        _supplyList.MinHeight = wide ? 0 : 100;
        _supplyList.MaxHeight = wide ? float.PositiveInfinity : 130;
        _supplyList.VerticalExpand = wide;
    }
    private List<Supply> Supplies()
    {
        var result = new List<Supply>();
        foreach (var c in _health.Care.Orders.Where(c => c.WorldId == SelectedWorldId))
            result.Add(new("care:" + c.Id, c.Prosthetic ? "Протезирование" : "Регенеративная помощь", c.Status, c.Completed, c.Progress,
                $"Пройдено {c.CompletedCourses:0.#}/{c.Courses:0.#} курсов" + (c.Working ? $". Осталось лечения: {c.HoursLeft:0.#} ч" : ""), c.Materials));
        foreach (var order in _health.Orders.Where(o => o.TargetWorldId == SelectedWorldId || o.ProducerWorldId == SelectedWorldId))
        {
            var target = _health.Worlds.FirstOrDefault(w => w.Id == order.TargetWorldId)?.Name ?? order.TargetWorldId;
            result.Add(new("order:" + order.Id, order.Name, order.Status, order.Completed, order.Progress, "Получатель: " + target, order.Materials, SWHealthDeliveryText.ProducedPayload(order)));
        }
        var world = _health.Worlds.FirstOrDefault(w => w.Id == SelectedWorldId);
        if (world != null)
            foreach (var p in world.Programs.Where(p => p.Started || p.Applied))
                result.Add(new("program:" + world.Id + ":" + p.Id, p.Name, p.Status, p.Applied,
                    p.WorkHours > 0 ? Math.Clamp(p.WorkedHours / p.WorkHours * 100, 0, 100) : p.Applied ? 100 : 0,
                    p.WorkHours > 0 ? $"Работа: {p.WorkedHours:0.#}/{p.WorkHours:0.#} ч" : "",
                    p.Materials.Select(m => new SWHealthMaterialView
                    {
                        GoodId = m.GoodId, Name = m.Name, Required = m.Required, Delivered = m.Delivered, Incoming = m.InTransit, StockKnown = false,
                        DeliveryHours = m.DeliveryHours, MinimumTravelHours = m.MinimumTravelHours,
                        NextDeliveryHours = m.NextDeliveryHours, NextMinimumTravelHours = m.NextMinimumTravelHours,
                        NextDeliveryQuantity = m.NextDeliveryQuantity, DeliveryStatus = m.DeliveryStatus,
                    }).ToList()));
        return result;
    }
    private void OpenSupply(string id)
    {
        _selectedSupply = id;
        _showCompleted = Supplies().FirstOrDefault(s => s.Id == id)?.Completed ?? false;
        SelectSection(2);
    }
    private void RefreshSupply()
    {
        SWConsoleStyle.StyleTabButton(_currentSupplies, !_showCompleted);
        SWConsoleStyle.StyleTabButton(_finishedSupplies, _showCompleted);
        var supplies = Supplies().Where(s => s.Completed == _showCompleted).ToArray();
        if (!supplies.Any(s => s.Id == _selectedSupply)) _selectedSupply = supplies.FirstOrDefault()?.Id ?? "";
        if (!_supplyButtons.Keys.SequenceEqual(supplies.Select(s => s.Id)))
        {
            _supplyRows.RemoveAllChildren();
            _supplyButtons.Clear();
            foreach (var entry in supplies)
            {
                var id = entry.Id;
                var button = SWConsoleStyle.TabButton("");
                button.Name = "HealthSupply_" + id;
                button.Label.ClipText = true;
                button.MinHeight = 50;
                button.OnPressed += _ => { _selectedSupply = id; RefreshSupply(); };
                _supplyButtons.Add(id, button);
                _supplyRows.AddChild(button);
            }
        }
        foreach (var entry in supplies)
        {
            var button = _supplyButtons[entry.Id];
            button.Text = entry.Title + "\n" + entry.Status;
            button.ToolTip = entry.Title + ": " + entry.Status;
            SWConsoleStyle.StyleTabButton(button, entry.Id == _selectedSupply);
        }
        _supplyDetails.RemoveAllChildren();
        var selected = supplies.FirstOrDefault(s => s.Id == _selectedSupply);
        if (selected == null)
        {
            _supplyDetails.AddChild(SWConsoleStyle.Wrap(_showCompleted ? "Завершённых заявок пока нет" : "Нет выполняемых заявок в этом мире", SWConsoleStyle.TextDim));
            return;
        }
        _supplyDetails.AddChild(SWConsoleStyle.Wrap(selected.Title, SWConsoleStyle.Brass));
        _supplyDetails.AddChild(SWConsoleStyle.Wrap(selected.Status, selected.Completed ? SWConsoleStyle.Good : SWConsoleStyle.TextBright));
        if (selected.Detail != "") _supplyDetails.AddChild(SWConsoleStyle.Wrap(selected.Detail, SWConsoleStyle.TextDim));
        _supplyDetails.AddChild(SWConsoleStyle.Bar(selected.Progress, 0, 100, SWConsoleStyle.Accent));
        for (var i = 0; i < selected.Materials.Count; i++)
            _supplyDetails.AddChild(SWHealthResearchPanel.MaterialCard(selected.Materials[i], i, "HealthNetworkSupplyMaterial" + i, paused: !_health.DayActive));
        if (selected.Payload != null)
            _supplyDetails.AddChild(SWHealthResearchPanel.MaterialCard(selected.Payload, 0, "HealthNetworkPayload", showStock: false, paused: !_health.DayActive));
    }
}
