using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared._SW._Senate.Economy;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;
using Robust.Shared.Utility;
using static Content.Client._SW._Senate.Consoles.Statistics.SWStatisticsWidgets;

namespace Content.Client._SW._Senate.Consoles.Statistics;

public sealed partial class SWRepublicMapBrowser
{
    private readonly Dictionary<string, GovernorCard> _governorCards = new();

    private void AddGovernorCard(string? sector)
    {
        var governor = _economyView?.Governors.FirstOrDefault(g => g.Sector == sector);
        if (sector == null || governor == null) return;
        if (!_governorCards.TryGetValue(sector, out var card))
            _governorCards[sector] = card = new GovernorCard();
        card.Update(governor);
        card.Panel.Orphan();
        _detail.AddChild(card.Panel);
    }

    /// <summary>Persistent per-sector controls retain expanded history across live snapshots and map navigation.</summary>
    private sealed class GovernorCard
    {
        public readonly PanelContainer Panel = SWConsoleStyle.Row();
        private readonly RichTextLabel _name = SWConsoleStyle.Wrap(string.Empty, SWConsoleStyle.Brass);
        private readonly RichTextLabel _status = SWConsoleStyle.Wrap(string.Empty, SWConsoleStyle.TextDim);
        private readonly RichTextLabel _traits = SWConsoleStyle.Wrap(string.Empty, SWConsoleStyle.TextBright);
        private readonly RichTextLabel _budget = SWConsoleStyle.Wrap(string.Empty, SWConsoleStyle.Accent);
        private readonly RichTextLabel _project = SWConsoleStyle.Wrap(string.Empty, SWConsoleStyle.TextBright);
        private readonly RichTextLabel _decision = SWConsoleStyle.Wrap(string.Empty, SWConsoleStyle.TextDim);
        private readonly RichTextLabel _appeal = SWConsoleStyle.Wrap(string.Empty, SWConsoleStyle.Warn);
        private readonly SWStatisticsBar _progress = new() { Name = "GovernorProjectProgress", Maximum = 1, Fill = SWConsoleStyle.Accent, MinHeight = 4, MaxHeight = 4 };
        private readonly Button _historyToggle = SWConsoleStyle.TabButton("Последние решения");
        private readonly BoxContainer _history = VerticalStack(4);
        private readonly RichTextLabel[] _events = new RichTextLabel[3];

        public GovernorCard()
        {
            Panel.Name = "GovernorCard";
            var body = VerticalStack(6);
            Panel.AddChild(body);
            _name.Name = "GovernorName";
            _budget.Name = "GovernorBudget";
            _appeal.Name = "GovernorAppeal";
            _decision.Name = "GovernorDecision";
            _project.Name = "GovernorProject";
            body.AddChild(SWConsoleStyle.Text("ГУБЕРНАТОР", SWConsoleStyle.TextDim, SWConsoleStyle.Small));
            body.AddChild(_name);
            body.AddChild(_traits);
            body.AddChild(_budget);
            body.AddChild(_project);
            body.AddChild(_progress);
            body.AddChild(_status);
            body.AddChild(_decision);
            body.AddChild(_appeal);
            _historyToggle.Name = "GovernorHistoryToggle";
            _historyToggle.TextAlign = Label.AlignMode.Left;
            _history.Visible = false;
            _history.Name = "GovernorHistory";
            _historyToggle.OnPressed += _ =>
            {
                _history.Visible = !_history.Visible;
                _historyToggle.Text = _history.Visible ? "Скрыть решения" : "Последние решения";
            };
            body.AddChild(_historyToggle);
            body.AddChild(_history);
            for (var i = 0; i < _events.Length; i++)
            {
                _events[i] = SWConsoleStyle.Wrap(string.Empty, SWConsoleStyle.TextDim);
                _history.AddChild(_events[i]);
            }
        }

        public void Update(SWGovernorView state)
        {
            Set(_name, state.Name, SWConsoleStyle.Brass);
            Set(_traits, string.Join(", ", state.Traits.Take(3)), SWConsoleStyle.TextBright);
            Set(_budget, $"Бюджет {state.Budget:N0} кр.    Резерв {state.Reserve:N0} кр.", SWConsoleStyle.Accent);
            var world = state.WorldName.Length > 0 ? state.WorldName : state.World;
            var project = state.Project.Length > 0 ? state.Project + (world.Length > 0 ? $"\n{world}" : string.Empty) : string.Empty;
            Set(_project, project, SWConsoleStyle.TextBright);
            _progress.Visible = state.Project.Length > 0;
            _progress.Value = Math.Clamp(state.ProjectProgress, 0, 1);
            _progress.ToolTip = $"Готовность проекта: {_progress.Value * 100:0}%";
            Set(_status, state.Status + (state.ProjectStatus.Length > 0 ? $"\n{state.ProjectStatus}" : string.Empty), SWConsoleStyle.TextDim);
            Set(_decision, state.Priority.Length > 0 ? $"Приоритет: {state.Priority}\nАктивный час: {Number(state.LastDecisionHour)}" : string.Empty, SWConsoleStyle.TextDim);
            Set(_appeal, state.HasRequest ? $"Обращение в Сенат: {state.RequestedCredits:N0} кр." : string.Empty, SWConsoleStyle.Warn);
            _historyToggle.Visible = state.History.Count > 0;
            var recent = state.History.TakeLast(3).Reverse().ToArray();
            for (var i = 0; i < _events.Length; i++)
                Set(_events[i], i < recent.Length ? recent[i] : string.Empty, SWConsoleStyle.TextDim);
        }

        private static void Set(RichTextLabel label, string text, Color color)
        {
            label.Visible = text.Length > 0;
            var message = new FormattedMessage();
            message.PushColor(color);
            message.AddText(text);
            message.Pop();
            label.SetMessage(message);
        }
    }
}
