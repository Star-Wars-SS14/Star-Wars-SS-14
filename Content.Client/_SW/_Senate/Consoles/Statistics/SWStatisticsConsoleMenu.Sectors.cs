using System;
using System.Collections.Generic;
using System.Numerics;
using System.Linq;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;
using static Content.Client._SW._Senate.Consoles.Statistics.SWStatisticsWidgets;

namespace Content.Client._SW._Senate.Consoles.Statistics;

public sealed partial class SWStatisticsConsoleMenu
{
    private bool _strongFirst;
    private string _sectorDetailSignature = string.Empty;
    private readonly Dictionary<string, float> _changedSectorRows = new();
    private readonly OptionButton _sectorMetric = new() { MinWidth = 190 };
    private readonly LineEdit _sectorSearch = new() { PlaceHolder = "Найти сектор…", MinWidth = 190 };
    private readonly Button _sort = SWConsoleStyle.TabButton("По возрастанию");
    private readonly Label _sectorReference = SWConsoleStyle.Text(string.Empty, SWConsoleStyle.Brass, SWConsoleStyle.Small);
    private readonly BoxContainer _ranking = VerticalStack(4);
    private readonly BoxContainer _sectorDetail = VerticalStack(10);
    private readonly Dictionary<string, SectorRow> _sectorRows = new();

    private void InitializeSectors()
    {
        var filters = new WrapContainer { HorizontalExpand = true };
        _sectorMetric.ModulateSelfOverride = Color.White;
        _sectorMetric.StyleBoxOverride = new SWConsoleSurface
        {
            Background = SWConsoleStyle.PillDim,
            ContentMarginLeftOverride = 12, ContentMarginRightOverride = 12,
            ContentMarginTopOverride = 7, ContentMarginBottomOverride = 7,
        };
        _sectorDetail.Margin = new Thickness(0, 0, 12, 0);
        _sectorMetric.Margin = new Thickness(0, 0, 8, 4);
        _sectorSearch.Margin = new Thickness(0, 0, 8, 4);
        filters.AddChild(_sectorMetric);
        filters.AddChild(_sectorSearch);
        filters.AddChild(_sort);
        SectorTable.AddChild(filters);
        SectorTable.AddChild(_sectorReference);
        var register = VerticalStack(6);
        register.AddChild(SWStatisticsText.Wrap("Сектор / показатель / представительство", SWConsoleStyle.TextDim));
        register.AddChild(_ranking);
        SectorTable.AddChild(new SWConsoleColumns(PagePanel(register), PagePanel(_sectorDetail), 300, 740));
        _sectorMetric.OnItemSelected += args =>
        {
            _sectorMetric.SelectId(args.Id);
            if (_state != null && args.Id >= 0 && args.Id < _state.Globals.Count) SelectMetric(_state.Globals[args.Id].Id);
        };
        _sectorSearch.OnTextChanged += _ => { _filterReveal = 0; RefreshSectors(); };
        _sort.OnPressed += _ => { _filterReveal = 0; _strongFirst = !_strongFirst; _sort.Text = _strongFirst ? "По убыванию" : "По возрастанию"; RefreshSectors(); };
    }

    private void RefreshSectors()
    {
        if (_state == null) return;
        var g = _state.Globals.FirstOrDefault(g => g.Id == _metricId);
        _sectorReference.Text = g == null ? string.Empty : $"Республика: {SWStatisticsPresentation.Number(g.Actual)}";
        var sectors = _state.Sectors.Where(s => s.Name.Contains(_sectorSearch.Text, StringComparison.OrdinalIgnoreCase)).ToList();
        sectors = sectors.OrderBy(s =>
        {
            var stat = g == null ? null : SWStatisticsPresentation.SectorMetric(s, g.Id);
            var normalized = stat == null ? null : SWStatisticsPresentation.Normalize(stat.Value, g!.Min, g.Max);
            return normalized.HasValue ? (_strongFirst ? -normalized.Value : normalized.Value) : float.PositiveInfinity;
        }).ThenBy(s => s.Name).ToList();
        if (!sectors.Any(s => s.Id == _sectorId)) _sectorId = sectors.FirstOrDefault()?.Id;
        foreach (var row in _sectorRows.Values) row.Button.Visible = false;
        foreach (var sector in sectors)
        {
            if (!_sectorRows.TryGetValue(sector.Id, out var row))
            {
                row = new SectorRow(sector.Name);
                row.Button.OnPressed += _ => SelectSector(sector.Id);
                _sectorRows.Add(sector.Id, row);
                _ranking.AddChild(row.Button);
            }
            var stat = g == null ? null : SWStatisticsPresentation.SectorMetric(sector, g.Id);
            row.Button.Visible = true;
            row.Button.SetPositionInParent(_ranking.ChildCount - 1);
            row.Button.SetAppearance(SWConsoleStyle.TextBright, selected: sector.Id == _sectorId, quiet: true);
            row.Name.SetMessage(sector.Name, defaultColor: SWConsoleStyle.TextBright);
            var value = SWStatisticsPresentation.Number(stat?.Value ?? float.NaN);
            if (!string.IsNullOrEmpty(row.Value.Text) && row.Value.Text != value) _changedSectorRows[sector.Id] = .10f;
            row.Value.Text = value;
            row.Bar.Value = stat?.Value ?? float.NaN;
            row.Bar.Minimum = g?.Min ?? 0;
            row.Bar.Maximum = g?.Max ?? 100;
            row.Bar.Reference = g?.Actual;
            row.Bar.Fill = stat == null ? SWConsoleStyle.TextDim : SWStatisticsPresentation.LevelColor(stat.Value, row.Bar.Minimum, row.Bar.Maximum);
            row.Status.Text = sector.Claimed ? "Представлен" : "Нет представителя";
            row.Button.ToolTip = sector.Claimed ? "Сектор представлен в Сенате" : "У сектора нет назначенного сенатора";
        }
        foreach (var id in _sectorRows.Keys.Where(id => _state.Sectors.All(s => s.Id != id)).ToArray())
        {
            _ranking.RemoveChild(_sectorRows[id].Button);
            _sectorRows.Remove(id);
            _changedSectorRows.Remove(id);
        }
        var selected = sectors.FirstOrDefault(s => s.Id == _sectorId);
        if (selected == null)
        {
            var emptyKey = _state.Sectors.Count == 0 ? "empty" : "not-found";
            if (_sectorDetailSignature == emptyKey) return;
            _sectorDetailSignature = emptyKey;
            _sectorDetail.RemoveAllChildren();
            _sectorDetail.AddChild(SWStatisticsText.Wrap(_state.Sectors.Count == 0 ? "Данных о секторах нет." : "Секторы не найдены.", SWConsoleStyle.TextDim));
            return;
        }
        var breakdown = _state.SituationalStats.Where(x => x.SectorId == selected.Id && x.GlobalId == g?.Id).ToArray();
        var anchor = _galaxy.GetUniqueSectorAnchor(selected.Id);
        var signature = (anchor?.Id ?? "no-unique-anchor") + "|" + (anchor?.Kind ?? "") + "|" + selected.Id + "|" + selected.Name + "|" + selected.Claimed + "|" + selected.Representative + "|" + g?.Id + "|" +
            string.Join(";", _state.Globals.Select(x => $"{x.Id}:{x.Name}:{x.Min}:{x.Max}:{x.Actual}")) + "|" +
            string.Join(";", selected.Stats.Select(x => $"{x.Id}:{x.Value}")) + "|" + SituationSignature(breakdown);
        if (signature == _sectorDetailSignature) return;
        _sectorDetailSignature = signature;
        _sectorDetail.RemoveAllChildren();
        _sectorDetail.AddChild(SWStatisticsText.Wrap(selected.Name, SWConsoleStyle.Brass));
        if (anchor != null)
        {
            var image = new SWStatisticsPlanetSymbol(anchor) { SetSize = new Vector2(64, 64), HorizontalAlignment = Control.HAlignment.Center };
            _sectorDetail.AddChild(image);
            _sectorDetail.AddChild(SWStatisticsText.Wrap(image.HasTexture ? $"Опорный мир: {anchor.Name}. Условное изображение." : $"Опорный мир: {anchor.Name}. Изображение недоступно.", SWConsoleStyle.TextDim));
        }
        else _sectorDetail.AddChild(SWStatisticsText.Wrap("Единственный опорный мир не определён.", SWConsoleStyle.TextDim));
        _sectorDetail.AddChild(SWStatisticsText.Wrap(selected.Claimed ? "Представлен в Сенате" : "Сенатор не назначен", SWConsoleStyle.TextDim));
        if (!string.IsNullOrWhiteSpace(selected.Representative))
            _sectorDetail.AddChild(SWStatisticsText.Wrap($"Представитель: {selected.Representative}", SWConsoleStyle.TextBright));
        foreach (var global in _state.Globals)
        {
            var stat = SWStatisticsPresentation.SectorMetric(selected, global.Id);
            _sectorDetail.AddChild(Metric(global.Name, stat?.Value ?? float.NaN, global.Min, global.Max, reference: global.Actual));
        }
        foreach (var entry in breakdown) _sectorDetail.AddChild(SWSituationalStatDisplay.Make(entry, _situationExpanded));
    }

    private void AdvanceSectorRows(float seconds, bool reduced)
    {
        foreach (var id in _changedSectorRows.Keys.ToArray())
        {
            var left = reduced ? 0 : Math.Max(0, _changedSectorRows[id] - Math.Max(0, seconds));
            if (_sectorRows.TryGetValue(id, out var row)) row.Button.Modulate = Color.White.WithAlpha(.75f + .25f * (1 - left / .10f));
            if (left <= 0) _changedSectorRows.Remove(id); else _changedSectorRows[id] = left;
        }
    }

    private sealed class SectorRow
    {
        public readonly SWConsoleButton Button = new() { HorizontalExpand = true };
        public readonly RichTextLabel Name;
        public readonly Label Value = SWConsoleStyle.Text(string.Empty, SWConsoleStyle.TextBright, SWConsoleStyle.Strong);
        public readonly SWStatisticsBar Bar = new();
        public readonly Label Status = SWConsoleStyle.Text(string.Empty, SWConsoleStyle.TextDim, SWConsoleStyle.Small);
        public SectorRow(string name)
        {
            Button.Label.Visible = false;
            Name = SWStatisticsText.Wrap(name, SWConsoleStyle.TextBright);
            var box = VerticalStack(4);
            var head = new BoxContainer { HorizontalExpand = true };
            head.AddChild(Name);
            head.AddChild(Value);
            box.AddChild(head);
            box.AddChild(Bar);
            box.AddChild(Status);
            Button.AddChild(box);
        }
    }
}
