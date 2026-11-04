using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Economy;
using Robust.Client.UserInterface.Controls;
using static Content.Client._SW._Senate.Consoles.Statistics.SWStatisticsWidgets;

namespace Content.Client._SW._Senate.Consoles.Statistics;

public sealed partial class SWRepublicMapBrowser
{
    private IReadOnlyList<SWMapCrisisEntry> _mapCrises = Array.Empty<SWMapCrisisEntry>();
    private string[] _selectedCrisisIds = Array.Empty<string>();
    private readonly Button _crisisButton = SWConsoleStyle.TabButton("Кризисы");
    public IReadOnlyList<string> SelectedCrisisIds => _selectedCrisisIds;

    public void SetCrises(IReadOnlyList<SWMapCrisisEntry> crises)
    {
        var edge = _route == null ? null : SWTransitLinkPrototype.LinkId(_route.From, _route.To);
        var before = _mapCrises.Where(c => _selectedCrisisIds.Length > 0
            ? _selectedCrisisIds.Contains(c.Id) : edge != null && c.Routes.Any(r => r.EdgeId == edge)).ToArray();
        var after = crises.Where(c => _selectedCrisisIds.Length > 0
            ? _selectedCrisisIds.Contains(c.Id) : edge != null && c.Routes.Any(r => r.EdgeId == edge)).ToArray();
        var detailsChanged = before.Length != after.Length || before.Zip(after, CrisisEquals).Any(equal => !equal);
        _mapCrises = crises;
        Map.SetCrises(crises, _data.Worlds);
        _crisisButton.Visible = true;
        _crisisButton.Text = $"Кризисы: {crises.Count}";
        _crisisButton.Disabled = crises.Count == 0;
        if (_selectedCrisisIds.Length > 0)
        {
            _selectedCrisisIds = _selectedCrisisIds.Where(id => crises.Any(c => c.Id == id)).ToArray();
            Map.SelectCrises(_selectedCrisisIds);
            if (detailsChanged) RefreshDetails();
        }
        else if (_route != null && detailsChanged) RefreshDetails();
    }

    private static bool CrisisEquals(SWMapCrisisEntry a, SWMapCrisisEntry b) =>
        a.Id == b.Id && a.Title == b.Title && a.Description == b.Description && a.Status == b.Status &&
        a.LocationLabel == b.LocationLabel && a.Consequences == b.Consequences && a.WorldId == b.WorldId &&
        a.EdgeId == b.EdgeId && a.Severity == b.Severity && a.EffectsLabel == b.EffectsLabel &&
        a.SectorIds.SequenceEqual(b.SectorIds) && a.Effects.Count == b.Effects.Count &&
        a.Effects.Zip(b.Effects, (x, y) => x.StatName == y.StatName && x.Color == y.Color && x.Delta == y.Delta && x.PerHour == y.PerHour).All(equal => equal) &&
        a.Routes.Count == b.Routes.Count && a.Routes.Zip(b.Routes, (x, y) => x.EdgeId == y.EdgeId &&
            x.CapacityMultiplier == y.CapacityMultiplier && x.BaseCapacity == y.BaseCapacity &&
            x.UsedSlotsPerHour == y.UsedSlotsPerHour && x.HasUsage == y.HasUsage).All(equal => equal);

    public void SelectCrises(IEnumerable<string> ids)
    {
        _selectedMedicalShipmentId = null;
        var selected = ids.Where(id => _mapCrises.Any(c => c.Id == id)).Distinct().ToArray();
        if (selected.Length == 0) return;
        if (Surface != null) SwitchView(CurrentSectorId);
        _selectedCrisisIds = selected;
        Map.SelectCrises(selected);
        RefreshDetails();
    }

    public void FocusCrisis(string id)
    {
        var crisis = _mapCrises.FirstOrDefault(c => c.Id == id);
        if (crisis == null) return;
        if (_fullPlanetScreen && _lockedSector == null)
        {
            if (crisis.WorldId.Length > 0 && _worlds.ContainsKey(crisis.WorldId))
                FocusWorldImmediately(crisis.WorldId);
            else
            {
                Map.FinishTransition();
                _systemParentSector = null;
                if (crisis.SectorIds.Count == 1 && _sectorPrototypes.Any(s => s.ID == crisis.SectorIds[0]))
                {
                    SwitchView(crisis.SectorIds[0]);
                    if (UniqueAnchor(crisis.SectorIds[0]) is { } anchor) SelectWorld(anchor.Id, true);
                }
                else SwitchView(null);
            }
            SelectCrises(new[] { id });
            _statisticsAtlas?.ShowDetails();
            return;
        }
        if (crisis.WorldId.Length > 0 && _worlds.ContainsKey(crisis.WorldId))
        {
            ShowGalaxy();
            SelectWorld(crisis.WorldId, true);
        }
        else if (crisis.SectorIds.Count == 1 && OpenSector(crisis.SectorIds[0]))
        {
            if (UniqueAnchor(crisis.SectorIds[0]) is { } anchor) SelectWorld(anchor.Id, true);
        }
        else ShowGalaxy();
        SelectCrises(new[] { id });
    }

    private void ClearCrisisSelection()
    {
        _selectedCrisisIds = Array.Empty<string>();
        Map.SelectCrises(_selectedCrisisIds);
    }

    private void RefreshCrisisDetails()
    {
        _detailHeading.RemoveAllChildren();
        _detail.RemoveAllChildren();
        var crises = _mapCrises.Where(c => _selectedCrisisIds.Contains(c.Id)).ToArray();
        _detailHeading.AddChild(SWConsoleStyle.Section(crises.Length == 1 ? "Кризис" : $"Кризисы: {crises.Length}"));
        var back = SWConsoleStyle.TabButton(_route != null ? "К сведениям о маршруте" : "К сведениям о мире");
        back.Name = "MapCrisisBack";
        back.OnPressed += _ => { ClearCrisisSelection(); RefreshDetails(); };
        _detailHeading.AddChild(back);
        foreach (var crisis in crises)
        {
            var card = VerticalStack(8);
            card.Name = "MapCrisis_" + crisis.Id;
            card.AddChild(SWConsoleStyle.Wrap(crisis.Title, SWConsoleStyle.Brass));
            card.AddChild(SWConsoleStyle.Wrap(crisis.LocationLabel, SWConsoleStyle.TextDim));
            card.AddChild(SWConsoleStyle.Wrap(crisis.Status, SWConsoleStyle.TextBright));
            card.AddChild(Metric("Острота", crisis.Severity, color: SWConsoleStyle.SeverityColor(crisis.Severity)));
            if (crisis.Description.Length > 0) card.AddChild(SWConsoleStyle.Wrap(crisis.Description, SWConsoleStyle.TextBright));
            if (crisis.Consequences.Length > 0) card.AddChild(SWConsoleStyle.Wrap(crisis.Consequences, SWConsoleStyle.TextDim));
            if (crisis.Effects.Count > 0)
            {
                card.AddChild(SWConsoleStyle.Section(crisis.EffectsLabel.Length > 0 ? crisis.EffectsLabel : "Влияние на показатели"));
                foreach (var effect in crisis.Effects)
                    card.AddChild(SWConsoleStyle.Wrap($"{effect.StatName}  {effect.Delta:+0.###;-0.###;0}", effect.Color));
            }
            foreach (var route in crisis.Routes)
            {
                var link = _transitLinks.GetValueOrDefault(route.EdgeId);
                var label = link == null ? route.EdgeId : $"{WorldName(link.From)} / {WorldName(link.To)}";
                card.AddChild(SWConsoleStyle.Section(label));
                var blocked = route.CapacityMultiplier <= 0;
                card.AddChild(SWConsoleStyle.Wrap(blocked ? "Перевозки заблокированы"
                    : route.CapacityMultiplier < .999 ? $"Пропускная способность −{(1 - route.CapacityMultiplier) * 100:0}%"
                    : "Ограничений перевозок нет", blocked ? SWConsoleStyle.Bad : SWConsoleStyle.Warn));
                card.AddChild(Pair("Доступно, мест/ч", Number(route.BaseCapacity * route.CapacityMultiplier), SWConsoleStyle.TextBright));
                // Observed traffic is a separate metric. It is not a counterfactual loss caused by this crisis.
                if (route.HasUsage)
                    card.AddChild(Pair("Фактическая загрузка, мест/ч", Number(route.UsedSlotsPerHour), SWConsoleStyle.TextDim));
            }
            var location = crisis.WorldId.Length > 0 ? _worlds.GetValueOrDefault(crisis.WorldId)
                : _fullPlanetScreen ? crisis.SectorIds.Count == 1 ? UniqueAnchor(crisis.SectorIds[0]) : null
                : _data.Worlds.FirstOrDefault(w => w.Anchor && crisis.SectorIds.Contains(w.Sector?.Id ?? ""));
            if (location != null)
            {
                var open = SWConsoleStyle.TabButton("Открыть место на карте");
                open.Name = "LocateMapCrisis_" + crisis.Id;
                open.OnPressed += _ =>
                {
                    if (location.Sector is { } sector) OpenSector(sector.Id);
                    else ShowGalaxy();
                    SelectWorldByUser(location.Id, true);
                    SelectCrises(new[] { crisis.Id });
                };
                card.AddChild(open);
            }
            _detail.AddChild(card);
        }
    }

    private void RouteCrisisDetails(string edge)
    {
        foreach (var crisis in _mapCrises.Where(c => c.Routes.Any(r => r.EdgeId == edge)))
        {
            var route = crisis.Routes.First(r => r.EdgeId == edge);
            _detail.AddChild(SWConsoleStyle.Wrap(route.CapacityMultiplier <= 0 ? "Блокада"
                : $"Пропускная способность −{(1 - route.CapacityMultiplier) * 100:0}%", SWConsoleStyle.Warn));
            var button = SWConsoleStyle.TabButton(crisis.Title);
            button.OnPressed += _ => SelectCrises(new[] { crisis.Id });
            _detail.AddChild(button);
        }
    }
}
