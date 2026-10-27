using System.Linq;
using System.Collections.Generic;
using Content.Shared._SW._Senate;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client._SW._Senate.Consoles.MinistryWork;

public sealed partial class SWMinistryWorkConsoleMenu
{
    private void BuildIndustrialEvents(SWMinistryWorkConsoleState state)
    {
        var active = state.EconEvents.FirstOrDefault(e => e.Active);
        if (active == null)
            RoutesContent.AddChild(SWConsoleStyle.Wrap("Сейчас экономических потрясений нет", SWConsoleStyle.TextDim));
        else
            AddEconomyEventCard(RoutesContent, active, state, true);

        var history = state.EconEvents.Where(e => !e.Active).Take(5).ToArray();
        if (history.Length == 0) return;
        RoutesContent.AddChild(SWConsoleStyle.Section("Последние потрясения"));
        foreach (var entry in history)
        {
            var location = string.IsNullOrEmpty(entry.Edge) ? entry.WorldName : $"{entry.AName} — {entry.BName}";
            RoutesContent.AddChild(SWConsoleStyle.Wrap($"{entry.Title} — {location}\n{entry.Status}\n{entry.Materials}\n{entry.Work}", SWConsoleStyle.TextDim));
        }
    }

    private void AddEconomyEventCard(BoxContainer target, SWEconomyEventView entry, SWMinistryWorkConsoleState state, bool showMap)
    {
        var location = string.IsNullOrEmpty(entry.Edge)
            ? entry.WorldName == entry.SectorName ? entry.WorldName : $"{entry.WorldName}, {entry.SectorName}"
            : $"{entry.AName} — {entry.BName}";
        var summary = SWConsoleStyle.Row(SWConsoleStyle.BgPanel, SWConsoleStyle.Accent);
        var summaryContent = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, Margin = new Thickness(8) };
        summaryContent.AddChild(SWConsoleStyle.Wrap(entry.Title, SWConsoleStyle.Brass));
        summaryContent.AddChild(SWConsoleStyle.Wrap(location, SWConsoleStyle.TextDim));
        summaryContent.AddChild(SWConsoleStyle.Wrap(entry.Effect, SWConsoleStyle.TextBright));
        summaryContent.AddChild(SWConsoleStyle.Wrap($"{entry.Detail}\n{entry.Status}\nОсталось: {entry.RemainingHours:0.#} игровых ч", SWConsoleStyle.TextDim));
        summary.AddChild(summaryContent);
        target.AddChild(summary);
        if (!string.IsNullOrEmpty(entry.ProjectId))
        {
            var delivery = SWConsoleStyle.Row();
            var deliveryContent = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, Margin = new Thickness(8) };
            deliveryContent.AddChild(SWConsoleStyle.Section("Выполнение и поставки"));
            deliveryContent.AddChild(SWConsoleStyle.Wrap($"{entry.Materials}\n{entry.Work}", SWConsoleStyle.TextBright));
            delivery.AddChild(deliveryContent);
            target.AddChild(delivery);
        }
        else
        {
            var actions = new List<Button>();
            AddEconomyEventOption(target, entry, entry.Standard, state, false, actions);
            AddEconomyEventOption(target, entry, entry.Fast, state, true, actions);
        }
        if (!showMap)
        {
            var back = SWConsoleStyle.TabButton("К сведениям карты");
            back.OnPressed += _ =>
            {
                if (string.IsNullOrEmpty(entry.Edge)) _economyMap?.SelectWorld(entry.World);
                else _economyMap?.SelectEconomyRoute(entry.Edge);
            };
            target.AddChild(back);
        }
        if (!showMap || !entry.Active) return;
        var map = SWConsoleStyle.Action("На карте", SWConsoleStyle.Brass);
        map.Name = $"EconomyEventMap_{entry.Id}";
        map.OnPressed += _ =>
        {
            if (_economyMap == null) return;
            if (string.IsNullOrEmpty(entry.Edge))
            {
                if (!string.IsNullOrEmpty(entry.Sector)) _economyMap.OpenSector(entry.Sector);
                _economyMap.SelectWorld(entry.World, true);
            }
            else if (!_economyMap.SelectEconomyRoute(entry.Edge)) return;
            _economyMap.SelectCrises(new[] { entry.CrisisId });
            ShowTab(ProjectsTab);
        };
        target.AddChild(map);
    }

    private void AddEconomyEventOption(BoxContainer target, SWEconomyEventView entry, SWEconomyEventOptionView option,
        SWMinistryWorkConsoleState state, bool accelerated, List<Button> actions)
    {
        var label = entry.Kind == "demand"
            ? accelerated ? "Ускоренная поставка" : "Стандартная поставка"
            : accelerated ? "Ускоренное восстановление" : "Стандартное восстановление";
        var status = !state.HasCard || !state.IsMinisterCard ? "Требуется карта министра" : option.Status;
        var row = SWConsoleStyle.Row();
        var content = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, Margin = new Thickness(8) };
        content.AddChild(SWConsoleStyle.Wrap($"{label} — {option.Cost:N0} кр", SWConsoleStyle.Brass));
        content.AddChild(SWConsoleStyle.Wrap($"Материалы: {option.Materials}\nРаботы после доставки: {option.WorkHours:0.#} ч", SWConsoleStyle.TextBright));
        if (status != "Доступно") content.AddChild(SWConsoleStyle.Wrap(status, SWConsoleStyle.TextDim));
        var action = SWConsoleStyle.Action(label, SWConsoleStyle.Accent);
        action.HorizontalAlignment = Control.HAlignment.Left;
        action.Name = $"EconomyEvent{(accelerated ? "Fast" : "Standard")}_{entry.Id}";
        action.Disabled = !entry.Active || !option.Available || !state.HasCard || !state.IsMinisterCard;
        action.ToolTip = status;
        action.OnPressed += _ =>
        {
            foreach (var button in actions) button.Disabled = true;
            _owner.RespondEconomyEvent(entry.Id, accelerated);
        };
        actions.Add(action);
        content.AddChild(action);
        row.AddChild(content);
        target.AddChild(row);
    }
}
