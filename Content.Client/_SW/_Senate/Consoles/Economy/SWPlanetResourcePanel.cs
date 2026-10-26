using System.Globalization;
using System.Linq;
using Content.Shared._SW._Senate.Economy;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;

namespace Content.Client._SW._Senate.Consoles.Economy;

/// <summary>Geography and industrial capabilities; live rates belong to the goods table.</summary>
public sealed class SWPlanetResourcePanel : BoxContainer
{
    public SWPlanetResourcePanel(string worldId)
    {
        Name = "PlanetResourcePanel";
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        SeparationOverride = 4;
        var prototypes = IoCManager.Resolve<IPrototypeManager>();
        if (!prototypes.TryIndex<SWPlanetEconomyPrototype>(worldId, out var planet))
            return;

        if (!planet.ActiveEconomy)
        {
            AddChild(SWConsoleStyle.Wrap("Вне производственной сети Республики", SWConsoleStyle.TextDim));
            return;
        }

        AddChild(SWConsoleStyle.Section("Ресурсы"));
        foreach (var resource in planet.NaturalResources)
        {
            var row = SWConsoleStyle.Row();
            row.Name = $"PlanetResource_{resource.Condition.Id}";
            var body = new BoxContainer
            {
                Orientation = LayoutOrientation.Vertical,
                HorizontalExpand = true,
                SeparationOverride = 3,
            };
            body.AddChild(SWConsoleStyle.Wrap(prototypes.Index(resource.Condition).Name, SWConsoleStyle.TextBright));
            row.AddChild(body);
            AddChild(row);
        }
        if (planet.NaturalResources.Count == 0)
            AddChild(SWConsoleStyle.Wrap("Местные источники не разведаны", SWConsoleStyle.TextDim));

        AddChild(SWConsoleStyle.Section("Промышленный профиль"));
        AddChild(SWConsoleStyle.Wrap(string.Join(", ", planet.Facilities.Select(f => prototypes.Index(f).Name)), SWConsoleStyle.TextDim));
        AddChild(SWConsoleStyle.Section("Мощности"));
        AddChild(SWConsoleStyle.Wrap($"Базовая энергия: {Number(planet.BaseloadPower)}\nТруд: {Number(planet.LaborCapacity)}", SWConsoleStyle.TextDim));
    }

    private static string Number(double value) => value < 0.01 && value > 0
        ? value.ToString("0.####", CultureInfo.InvariantCulture)
        : value.ToString("0.##", CultureInfo.InvariantCulture);
}
