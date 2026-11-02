using System;
using Content.Shared._SW._Senate;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;
using static Robust.Client.UserInterface.Controls.BoxContainer;

namespace Content.Client._SW._Senate;

/// <summary>A static, immediately skippable opening for the Senate's public round history.</summary>
public sealed class SWSenateFinale : BoxContainer
{
    public SWSenateFinale(SWSenateRoundHistory history, TimeSpan duration, Action showDetails)
    {
        Name = "Последнее заседание";
        Orientation = LayoutOrientation.Vertical;

        var background = new PanelContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            PanelOverride = new StyleBoxFlat { BackgroundColor = Color.FromHex("#101820") },
        };
        AddChild(background);
        var layout = new BoxContainer { Orientation = LayoutOrientation.Vertical, Margin = new Thickness(28, 20), SeparationOverride = 10 };
        background.AddChild(layout);
        layout.AddChild(SWConsoleStyle.Text("ПОСЛЕДНЕЕ ЗАСЕДАНИЕ", SWConsoleStyle.Brass, SWConsoleStyle.Small));
        layout.AddChild(SWConsoleStyle.Text("Созыв завершён", SWConsoleStyle.TextBright, SWConsoleStyle.Title));
        layout.AddChild(SWConsoleStyle.Wrap($"Игровых дней: {history.Days}  ·  Длительность: {(int) duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}  ·  Принятых решений: {history.Laws.Length + history.AdditionalLaws}", SWConsoleStyle.TextDim));

        var details = new Button { Text = "К итогам", HorizontalAlignment = HAlignment.Left };
        details.OnPressed += _ => showDetails();
        layout.AddChild(details);

        var columns = new BoxContainer { Orientation = LayoutOrientation.Horizontal, VerticalExpand = true, SeparationOverride = 30 };
        layout.AddChild(columns);
        var participants = new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, SeparationOverride = 8 };
        columns.AddChild(participants);
        participants.AddChild(SWConsoleStyle.Text("УЧАСТНИКИ", SWConsoleStyle.Brass, SWConsoleStyle.Strong));
        participants.AddChild(SWConsoleStyle.Wrap("Публичные имена в конце созыва. Портреты недоступны.", SWConsoleStyle.TextDim, 350));
        var scroll = new ScrollContainer { VerticalExpand = true, HorizontalExpand = true, HScrollEnabled = false };
        participants.AddChild(scroll);
        var content = new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, SeparationOverride = 8 };
        scroll.AddChild(content);
        if (history.Participants.Length == 0)
            content.AddChild(SWConsoleStyle.Wrap("Участники не зафиксированы.", SWConsoleStyle.TextBright));
        foreach (var participant in history.Participants)
            content.AddChild(SWConsoleStyle.Wrap(participant, SWConsoleStyle.TextBright, 350));

        var facts = new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, SeparationOverride = 10 };
        columns.AddChild(facts);
        facts.AddChild(SWConsoleStyle.Text("РЕШЕНИЯ СОЗЫВА", SWConsoleStyle.Brass, SWConsoleStyle.Strong));
        facts.AddChild(SWConsoleStyle.Wrap($"Принято законов: {history.PassedLaws}", SWConsoleStyle.TextBright, 350));
        facts.AddChild(SWConsoleStyle.Wrap($"Принято поправок: {history.PassedAmendments}", SWConsoleStyle.TextBright, 350));
        facts.AddChild(SWConsoleStyle.Wrap("Подробная история решений доступна в итогах.", SWConsoleStyle.TextDim, 350));
    }
}
