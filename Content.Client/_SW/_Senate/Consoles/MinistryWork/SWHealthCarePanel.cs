using System;
using System.Linq;
using Content.Shared._SW._Senate.Ministries;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._SW._Senate.Consoles.MinistryWork;

/// <summary>Addressed elective-care sessions share the existing medical network world selection.</summary>
public sealed class SWHealthCarePanel : BoxContainer
{
    private SWHealthView _health = new();
    private readonly BoxContainer _needs = Stack();
    private readonly Label _teams = SWConsoleStyle.Text("", SWConsoleStyle.TextBright, SWConsoleStyle.Body);
    private readonly Label _openOrders = SWConsoleStyle.Text("", SWConsoleStyle.TextBright, SWConsoleStyle.Body);
    private readonly Label _history = SWConsoleStyle.Text("", SWConsoleStyle.TextDim, SWConsoleStyle.Small);
    private readonly Label _courses = SWConsoleStyle.Text("", SWConsoleStyle.TextDim, SWConsoleStyle.Small);
    private readonly Label _effect = SWConsoleStyle.Text("", SWConsoleStyle.TextDim, SWConsoleStyle.Small);
    public string SelectedWorldId { get; private set; } = "";
    public event Action<SWHealthActionMessage>? ActionRequested;
    public event Action<string>? OrderSelected;
    public SWHealthCarePanel()
    {
        Name = "HealthPlannedCare";
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        SeparationOverride = 8;
        var summary = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 24 };
        summary.AddChild(_teams);
        summary.AddChild(_openOrders);
        AddChild(summary);
        AddChild(_effect);
        AddChild(_needs);
        var history = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 24 };
        history.AddChild(_history);
        history.AddChild(_courses);
        AddChild(history);
    }
    private static BoxContainer Stack() => new() { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, SeparationOverride = 6 };
    protected override void Resized()
    {
        base.Resized();
        ArrangeNeeds();
    }
    private void ArrangeNeeds()
    {
        _needs.Orientation = Width >= 560 ? LayoutOrientation.Horizontal : LayoutOrientation.Vertical;
        foreach (var child in _needs.Children)
            child.MaxWidth = Width >= 560 ? Math.Max(1, (Width - 6) / 2) : float.PositiveInfinity;
    }
    public void SetState(SWHealthView health, string? selectedWorld = null)
    {
        _health = health;
        if (selectedWorld != null && health.Worlds.Any(w => w.Id == selectedWorld)) SelectedWorldId = selectedWorld;
        if (!health.Worlds.Any(w => w.Id == SelectedWorldId))
            SelectedWorldId = health.Care.Needs.OrderByDescending(n => n.WaitingCourses - n.ReservedCourses).Select(n => n.WorldId)
                .FirstOrDefault(id => health.Worlds.Any(w => w.Id == id)) ?? health.Worlds.FirstOrDefault()?.Id ?? "";
        Rebuild();
    }
    public void SelectWorld(string world)
    {
        if (!_health.Worlds.Any(w => w.Id == world)) return;
        SelectedWorldId = world;
        Rebuild();
    }
    private void Rebuild()
    {
        var care = _health.Care;
        var sector = _health.Worlds.FirstOrDefault(w => w.Id == SelectedWorldId)?.SectorId ?? "";
        _effect.Visible = care.SectorHealthcareEffects.TryGetValue(sector, out var effect);
        _effect.Text = $"Влияние очереди на медицину сектора: {effect:0.##}";
        _teams.Text = $"Бригады: {care.BusyTeams}/{care.TeamLimit}";
        _openOrders.Text = $"Открытые заявки: {care.OpenOrders}/{care.OrderLimit}";
        _history.Text = $"Завершено заявок: {care.CompletedCampaigns}";
        _courses.Text = $"Пройдено курсов: {care.CompletedCourses:0.#}";
        _needs.RemoveAllChildren();
        foreach (var prosthetic in new[] { false, true })
        {
            var need = care.Needs.FirstOrDefault(n => n.WorldId == SelectedWorldId && n.Prosthetic == prosthetic);
            var card = SWConsoleStyle.Row();
            card.Name = prosthetic ? "HealthCareNeedProsthetic" : "HealthCareNeedRegenerative";
            var body = Stack();
            body.SeparationOverride = 3;
            body.AddChild(SWConsoleStyle.Wrap(prosthetic ? "Протезирование" : "Регенеративная помощь", SWConsoleStyle.Brass));
            if (need == null)
                body.AddChild(SWConsoleStyle.Wrap("Нет сведений об очереди", SWConsoleStyle.TextDim));
            else
            {
                var active = care.Orders.FirstOrDefault(o => o.WorldId == SelectedWorldId && o.Prosthetic == prosthetic && !o.Completed);
                body.AddChild(MetricRow("Очередь", $"{need.WaitingCourses:0.#} курсов"));
                body.AddChild(MetricRow("Назначено", $"{need.ReservedCourses:0.#} курсов"));
                if (active == null)
                {
                    body.AddChild(MetricRow("Новая заявка", $"{need.SessionCourses:0.#} курсов, {need.WorkHours:0.#} ч"));
                    for (var i = 0; i < need.Materials.Count; i++) body.AddChild(RequirementRow(need.Materials[i]));
                }
                else
                {
                    body.AddChild(SWConsoleStyle.Wrap(active.Status, SWConsoleStyle.TextBright));
                    body.AddChild(SWConsoleStyle.Bar(active.Progress, 0, 100, SWConsoleStyle.Accent));
                    body.AddChild(SWConsoleStyle.Wrap($"Пройдено {active.CompletedCourses:0.#}/{active.Courses:0.#} курсов", SWConsoleStyle.TextDim));
                    if (active.Working) body.AddChild(SWConsoleStyle.Wrap($"Осталось лечения: {active.HoursLeft:0.#} ч", SWConsoleStyle.TextDim));
                    var details = SWConsoleStyle.Action("Снабжение и ход лечения", SWConsoleStyle.Accent);
                    details.Name = prosthetic ? "HealthCareDetailsProsthetic" : "HealthCareDetailsRegenerative";
                    details.OnPressed += _ => OrderSelected?.Invoke(active.Id);
                    body.AddChild(details);
                }
                var enabled = active == null && _health.CanManage && _health.MinisterActive && need.CanStart && _health.Budget >= need.Cost;
                var start = SWConsoleStyle.Action(active == null ? $"Начать за {need.Cost:N0} кр" : "Лечение назначено", SWConsoleStyle.Accent, !enabled);
                start.Name = prosthetic ? "HealthCareStartProsthetic" : "HealthCareStartRegenerative";
                var world = SelectedWorldId;
                start.OnPressed += _ => ActionRequested?.Invoke(new(SWHealthAction.StartPlannedCare, world, option: prosthetic));
                if (!enabled)
                {
                    var reason = active != null ? "Кампания уже заказана" : !need.CanStart && !string.IsNullOrEmpty(need.UnavailableReason) ? need.UnavailableReason
                        : !_health.CanManage || !_health.MinisterActive ? "Требуется действующий министр"
                        : _health.Budget < need.Cost ? $"Недостаточно средств: требуется {need.Cost:N0} кр" : need.UnavailableReason;
                    start.ToolTip = reason;
                    if (active == null && !string.IsNullOrEmpty(reason)) body.AddChild(SWConsoleStyle.Wrap(reason, SWConsoleStyle.Warn));
                }
                body.AddChild(start);
            }
            card.AddChild(body);
            _needs.AddChild(card);
        }
        ArrangeNeeds();
    }
    private static BoxContainer MetricRow(string label, string value)
    {
        var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal, HorizontalExpand = true, SeparationOverride = 12 };
        row.AddChild(SWConsoleStyle.Text(label, SWConsoleStyle.TextDim, SWConsoleStyle.Small));
        row.AddChild(new BoxContainer { HorizontalExpand = true });
        row.AddChild(SWConsoleStyle.Text(value, SWConsoleStyle.TextBright, SWConsoleStyle.Small));
        return row;
    }
    private static BoxContainer RequirementRow(SWHealthMaterialView material)
    {
        var row = Stack();
        var name = SWConsoleStyle.Wrap(material.Name, SWConsoleStyle.Brass);
        name.HorizontalExpand = true;
        row.AddChild(name);
        var quantities = new BoxContainer { Orientation = LayoutOrientation.Horizontal, HorizontalExpand = true, SeparationOverride = 8 };
        quantities.AddChild(SWConsoleStyle.Text($"Нужно {material.Required:0.###}", SWConsoleStyle.TextBright, SWConsoleStyle.Small));
        var stock = material.StockKnown ? material.RepublicStock.ToString("0.###") : "нет данных";
        var inventory = SWConsoleStyle.Text($"Склад {stock}", SWConsoleStyle.TextDim, SWConsoleStyle.Small);
        inventory.ToolTip = "Запас на складах Республики";
        quantities.AddChild(inventory);
        row.AddChild(quantities);
        return row;
    }
}
