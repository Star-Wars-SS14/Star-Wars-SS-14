using System;
using System.Collections.Generic;
using System.Linq;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;
using static Content.Client._SW._Senate.Consoles.Statistics.SWStatisticsWidgets;

namespace Content.Client._SW._Senate.Consoles.Statistics;

public sealed partial class SWStatisticsConsoleMenu
{
    private int _period = 360;
    private bool _showComponents;
    private bool _showObservations;
    private readonly RichTextLabel _brief = SWConsoleStyle.Wrap(string.Empty, SWConsoleStyle.TextDim);
    private readonly BoxContainer _analysis = VerticalStack(10);
    private readonly RichTextLabel _metricTitle = SWConsoleStyle.Wrap(string.Empty, SWConsoleStyle.TextBright);
    private readonly Label _value = SWConsoleStyle.Text(string.Empty, SWConsoleStyle.Accent, SWConsoleStyle.Title);
    private readonly Label _policy = SWConsoleStyle.Text(string.Empty, SWConsoleStyle.Brass, SWConsoleStyle.Body);
    private readonly RichTextLabel _change = SWConsoleStyle.Wrap(string.Empty, SWConsoleStyle.TextDim);
    private readonly SWStatisticsTrend _trend = new();
    private readonly Dictionary<int, Button> _periodButtons = new();
    private readonly Button _componentsButton = SWConsoleStyle.TabButton("Состав политики");
    private readonly Button _observationsButton = SWConsoleStyle.TabButton("История значений");
    private readonly BoxContainer _components = VerticalStack(10);
    private readonly BoxContainer _observations = VerticalStack(4);

    private void InitializeOverview()
    {
        RepublicStats.AddChild(_brief);
        _metricTitle.Stylesheet = new Stylesheet(new StyleRule[] { StylesheetHelpers.Element<RichTextLabel>().Prop("font", SWConsoleStyle.Title) });
        _analysis.AddChild(_metricTitle);
        var readings = new WrapContainer { HorizontalExpand = true };
        _value.Margin = new Thickness(0, 0, 24, 0);
        readings.AddChild(_value);
        readings.AddChild(_policy);
        _analysis.AddChild(readings);
        _analysis.AddChild(_change);
        var periods = new WrapContainer();
        foreach (var (minutes, title) in new[] { (360, "6 часов"), (720, "12 часов"), (0, "Весь журнал") })
        {
            var button = SWConsoleStyle.TabButton(title);
            button.OnPressed += _ => { _period = minutes; RefreshOverview(); };
            _periodButtons.Add(minutes, button);
            periods.AddChild(button);
        }
        _analysis.AddChild(periods);
        _analysis.AddChild(_trend);
        var legend = new BoxContainer { SeparationOverride = 10 };
        legend.AddChild(new SWStatisticsKey());
        legend.AddChild(SWConsoleStyle.Text("Факт", SWConsoleStyle.Accent, SWConsoleStyle.Small));
        legend.AddChild(new SWStatisticsKey { Dashed = true, Margin = new Thickness(14, 0, 0, 0) });
        legend.AddChild(SWConsoleStyle.Text("Политика", SWConsoleStyle.Brass, SWConsoleStyle.Small));
        _analysis.AddChild(legend);
        var disclosures = new WrapContainer();
        disclosures.AddChild(_componentsButton);
        disclosures.AddChild(_observationsButton);
        _componentsButton.OnPressed += _ => { _showComponents = !_showComponents; RefreshOverview(); };
        _observationsButton.OnPressed += _ => { _showObservations = !_showObservations; RefreshOverview(); };
        _analysis.AddChild(disclosures);
        _analysis.AddChild(_components);
        _analysis.AddChild(_observations);
        RepublicStats.AddChild(new SWConsoleColumns(_metrics, _analysis, 215, 700, fixedFirst: true));
    }

    private void RefreshOverview()
    {
        if (_state == null) return;
        var g = _state.Globals.FirstOrDefault(g => g.Id == _metricId);
        _analysis.Visible = g != null;
        if (g == null) { _brief.Visible = true; _brief.SetMessage("Нет данных"); return; }
        var weak = _state.Globals.Where(x => SWStatisticsPresentation.Normalize(x.Actual, x.Min, x.Max) is < 0.4f)
            .OrderBy(x => SWStatisticsPresentation.Normalize(x.Actual, x.Min, x.Max)).Select(x => x.Name).ToArray();
        var brief = weak.Length > 0 ? "Низкий уровень: " + string.Join(", ", weak.Take(3)) + (weak.Length > 3 ? $" и ещё {weak.Length - 3}" : string.Empty)
            : string.Empty;
        if (_state.PowersActive) brief += " Действуют чрезвычайные полномочия.";
        _brief.Visible = brief.Length > 0;
        _brief.SetMessage(brief, defaultColor: SWConsoleStyle.TextDim);
        _metricTitle.SetMessage(g.Name, defaultColor: SWConsoleStyle.TextBright);
        _value.Text = $"Факт {SWStatisticsPresentation.Number(g.Actual)}";
        _policy.Text = $"Политика {SWStatisticsPresentation.Number(g.Policy)}";
        _policy.ToolTip = "Сводный индекс политики";
        _value.ToolTip = "Средневзвешенный показатель секторов";
        var series = SWStatisticsPresentation.Series(_state, g, _period);
        var first = series.FirstOrDefault(p => p.Actual.HasValue);
        var enough = float.IsFinite(g.Actual) && series.Count(p => p.Actual.HasValue) > 1;
        _change.Visible = enough;
        _change.SetMessage(enough && first.Actual is { } initial
            ? $"Изменение: {g.Actual - initial:+0.#;-0.#;0}"
            : string.Empty, defaultColor: SWConsoleStyle.TextDim);
        _trend.SetData(series, g.Min, g.Max);
        foreach (var (minutes, button) in _periodButtons) SWConsoleStyle.StyleTabButton(button, minutes == _period);
        SWConsoleStyle.StyleTabButton(_componentsButton, _showComponents);
        SWConsoleStyle.StyleTabButton(_observationsButton, _showObservations);
        _components.Visible = _showComponents;
        _observations.Visible = _showObservations;
        if (_showComponents)
        {
            _components.RemoveAllChildren();
            foreach (var sub in g.Subs) _components.AddChild(Metric(sub.Name, sub.Value, sub.Min, sub.Max));
            if (g.Subs.Count == 0) _components.AddChild(SWConsoleStyle.Wrap("Подробных показателей нет.", SWConsoleStyle.TextDim));
        }
        if (_showObservations)
        {
            _observations.RemoveAllChildren();
            _observations.AddChild(SWConsoleStyle.Wrap("Время          Факт          Политика", SWConsoleStyle.TextDim));
            foreach (var p in series.AsEnumerable().Reverse())
                _observations.AddChild(SWConsoleStyle.Wrap($"{SWStatisticsPresentation.Time(p.Minute)}    {SWStatisticsPresentation.Number(p.Actual ?? float.NaN)}    {SWStatisticsPresentation.Number(p.Policy ?? float.NaN)}", SWConsoleStyle.TextBright));
        }
    }
}
