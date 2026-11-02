using System;
using Content.Shared._SW._Senate;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;
using static Robust.Client.UserInterface.Controls.BoxContainer;

namespace Content.Client._SW._Senate;

/// <summary>Manually paced, immediately skippable public Senate credits.</summary>
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
        participants.AddChild(SWConsoleStyle.Wrap("Публичные имена в конце созыва", SWConsoleStyle.TextDim, 350));
        var scroll = new ScrollContainer { VerticalExpand = true, HorizontalExpand = true, HScrollEnabled = false };
        participants.AddChild(scroll);
        var content = new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, SeparationOverride = 8 };
        scroll.AddChild(content);
        const int perPage = 4;
        var page = 0;
        var pageCount = Math.Max(1, (history.Participants.Length + perPage - 1) / perPage);
        var controls = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 8 };
        participants.AddChild(controls);
        var previous = new Button { Text = "Назад" };
        var next = new Button { Text = "Далее" };
        var replay = new Button { Text = "С начала" };
        var pageLabel = SWConsoleStyle.Text("", SWConsoleStyle.TextDim, SWConsoleStyle.Small);
        controls.AddChild(previous);
        controls.AddChild(next);
        controls.AddChild(replay);
        controls.AddChild(pageLabel);

        void ShowPage()
        {
            content.RemoveAllChildren();
            if (history.Participants.Length == 0)
                content.AddChild(SWConsoleStyle.Wrap("Участники не зафиксированы.", SWConsoleStyle.TextBright, 350));
            var end = Math.Min(history.Participants.Length, (page + 1) * perPage);
            for (var i = page * perPage; i < end; i++)
            {
                var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 12 };
                content.AddChild(row);
                var portrait = new PanelContainer
                {
                    SetSize = new System.Numerics.Vector2(48, 56),
                    PanelOverride = new StyleBoxFlat { BackgroundColor = Color.FromHex("#28343A") },
                };
                portrait.AddChild(new Label
                {
                    Text = "?",
                    FontOverride = SWConsoleStyle.Title,
                    FontColorOverride = SWConsoleStyle.TextDim,
                    HorizontalAlignment = HAlignment.Center,
                    VerticalAlignment = VAlignment.Center,
                });
                row.AddChild(portrait);
                var identity = new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, SeparationOverride = 3 };
                identity.AddChild(SWConsoleStyle.Wrap(history.Participants[i], SWConsoleStyle.TextBright, 280));
                identity.AddChild(SWConsoleStyle.Wrap("Портрет недоступен", SWConsoleStyle.TextDim, 280));
                row.AddChild(identity);
            }
            pageLabel.Text = $"{page + 1} / {pageCount}";
            previous.Disabled = page == 0;
            next.Disabled = page + 1 >= pageCount;
            replay.Disabled = page == 0;
        }

        previous.OnPressed += _ => { page--; ShowPage(); };
        next.OnPressed += _ => { page++; ShowPage(); };
        replay.OnPressed += _ => { page = 0; ShowPage(); };
        ShowPage();

        var factsScroll = new ScrollContainer { VerticalExpand = true, HorizontalExpand = true, HScrollEnabled = false };
        columns.AddChild(factsScroll);
        var facts = new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, SeparationOverride = 10 };
        factsScroll.AddChild(facts);
        facts.AddChild(SWConsoleStyle.Text("РЕШЕНИЯ СОЗЫВА", SWConsoleStyle.Brass, SWConsoleStyle.Strong));
        facts.AddChild(SWConsoleStyle.Wrap($"Принято законов: {history.PassedLaws}", SWConsoleStyle.TextBright, 350));
        facts.AddChild(SWConsoleStyle.Wrap($"Принято поправок: {history.PassedAmendments}", SWConsoleStyle.TextBright, 350));
        facts.AddChild(SWConsoleStyle.Wrap("Подробная история решений доступна в итогах.", SWConsoleStyle.TextDim, 350));
        if (history.CloseVotes.Length > 0)
        {
            facts.AddChild(SWConsoleStyle.Text("ЗАЛ РАЗДЕЛИЛСЯ", SWConsoleStyle.Brass, SWConsoleStyle.Strong));
            facts.AddChild(SWConsoleStyle.Wrap("Ближайшие голоса секторов по публичным законопроектам; решающий голос не включён.", SWConsoleStyle.TextDim, 350));
            foreach (var vote in history.CloseVotes)
                facts.AddChild(SWConsoleStyle.Wrap($"{vote.Name} — за: {vote.For}, против: {vote.Against}", SWConsoleStyle.TextBright, 350));
        }
    }
}
