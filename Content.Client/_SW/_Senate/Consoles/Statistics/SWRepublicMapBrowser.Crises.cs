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
        _mapCrises = crises;
        Map.SetCrises(crises, _data.Worlds);
        _crisisButton.Visible = true;
        _crisisButton.Text = $"Кризисы: {crises.Count}";
        _crisisButton.Disabled = crises.Count == 0;
        if (_selectedCrisisIds.Length > 0)
        {
            _selectedCrisisIds = _selectedCrisisIds.Where(id => crises.Any(c => c.Id == id)).ToArray();
            Map.SelectCrises(_selectedCrisisIds);
            RefreshDetails();
        }
        else if (_route != null) RefreshDetails();
    }

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
                : _data.Worlds.FirstOrDefault(w => w.Anchor && crisis.SectorIds.Contains(w.Sector?.Id ?? ""));
            if (location != null)
            {
                var open = SWConsoleStyle.TabButton("Открыть место на карте");
                open.Name = "LocateMapCrisis_" + crisis.Id;
                open.OnPressed += _ =>
                {
                    if (location.Sector is { } sector) OpenSector(sector.Id);
                    else ShowGalaxy();
                    SelectWorld(location.Id, true);
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
