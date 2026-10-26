using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Ministries;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._SW._Senate.Consoles.MinistryWork;

/// <summary>Live incidents are the operation queue; orders are issued against their actual map targets.</summary>
public sealed class SWDefenseOperationsPanel : BoxContainer
{
    private readonly BoxContainer _queue = Stack();
    private readonly BoxContainer _log = Stack();
    private readonly Label _summary = SWConsoleStyle.Text("", SWConsoleStyle.Accent, SWConsoleStyle.Strong);
    private readonly Label _empty = SWConsoleStyle.Text("Активных событий нет", SWConsoleStyle.TextDim, SWConsoleStyle.Body);
    private readonly Dictionary<int, EventRow> _events = new();
    private string? _logRevision;
    public event Action<int>? EventSelected;

    public SWDefenseOperationsPanel()
    {
        Name = "DefenseOperationQueue";
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        SeparationOverride = 6;
        AddChild(_summary);
        AddChild(SWConsoleStyle.Section("Очередь событий"));
        AddChild(_empty);
        AddChild(_queue);
        AddChild(SWConsoleStyle.Section("Журнал операций"));
        AddChild(_log);
    }

    private static BoxContainer Stack() => new() { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, SeparationOverride = 5 };

    public void SetState(SWDefenseMapState? state, IReadOnlyList<string> log)
    {
        var live = state?.Events.Where(e => !e.Resolved).OrderByDescending(e => e.Severity).ThenBy(e => e.ExpiresAt).ToArray()
            ?? Array.Empty<SWDefenseEventView>();
        _summary.Text = $"Активных событий: {live.Length}   Флотов в операции: {state?.Fleets.Count(f => f.EventId > 0) ?? 0}";
        _empty.Visible = live.Length == 0;
        var ids = live.Select(e => e.Id).ToHashSet();
        foreach (var id in _events.Keys.Where(id => !ids.Contains(id)).ToArray())
        {
            _events[id].Panel.Orphan();
            _events.Remove(id);
        }
        foreach (var incident in live)
        {
            if (!_events.TryGetValue(incident.Id, out var row))
            {
                var id = incident.Id;
                row = new EventRow();
                row.Open.OnPressed += _ => EventSelected?.Invoke(id);
                _events.Add(id, row);
                _queue.AddChild(row.Panel);
            }
            row.Title.Text = $"Событие {incident.Id}   {incident.Title}";
            row.Title.FontColorOverride = incident.Severity >= 60 ? SWConsoleStyle.Bad : SWConsoleStyle.Warn;
            var world = state?.Worlds.FirstOrDefault(w => w.Id == incident.WorldId)?.Name ?? incident.WorldId;
            row.Location.Text = $"{world}   Угроза {incident.Severity:0}   Сила противника {incident.EnemyStrength:0}";
            row.Location.ToolTip = incident.EdgeId;
            row.Deadline.Text = $"До эскалации: {SWSenateDuration.FormatHours(Math.Max(0, incident.ExpiresAt - (state?.NowHours ?? 0)))}";
            var assigned = state?.Fleets.Where(f => f.EventId == incident.Id).Select(f => $"{f.Name}: {SWDefenseMapPanel.PhaseName(f.Phase)}").ToArray();
            row.Status.Text = incident.Status + (assigned is { Length: > 0 } ? "\n" + string.Join("\n", assigned) : "");
            row.Risk.Text = incident.Risk;
            row.Risk.ToolTip = incident.Risk;
            row.Panel.SetPositionInParent(Array.IndexOf(live, incident));
        }
        var revision = string.Join("\n", log) + "\n" + string.Join(";", state?.Events.Where(e => e.Resolved).Select(e => $"{e.Id}:{e.Status}") ?? Array.Empty<string>());
        if (_logRevision == revision) return;
        _logRevision = revision;
        _log.RemoveAllChildren();
        foreach (var incident in state?.Events.Where(e => e.Resolved).OrderByDescending(e => e.Id) ?? Enumerable.Empty<SWDefenseEventView>())
            _log.AddChild(SWConsoleStyle.Wrap($"Событие {incident.Id}   {incident.Title}\n{incident.Status}", SWConsoleStyle.Good));
        foreach (var entry in log) _log.AddChild(SWConsoleStyle.Wrap(entry, SWConsoleStyle.TextDim));
        if (_log.ChildCount == 0) _log.AddChild(SWConsoleStyle.Text("Записей нет", SWConsoleStyle.TextDim, SWConsoleStyle.Small));
    }

    private sealed class EventRow
    {
        public readonly PanelContainer Panel = SWConsoleStyle.Row();
        public readonly Label Title = SWConsoleStyle.Text("", SWConsoleStyle.Warn, SWConsoleStyle.Strong);
        public readonly Label Location = SWConsoleStyle.Text("", SWConsoleStyle.TextDim, SWConsoleStyle.Small);
        public readonly Label Deadline = SWConsoleStyle.Text("", SWConsoleStyle.Warn, SWConsoleStyle.Small);
        public readonly Label Status = SWConsoleStyle.Text("", SWConsoleStyle.TextBright, SWConsoleStyle.Small);
        public readonly Label Risk = SWConsoleStyle.Text("", SWConsoleStyle.TextDim, SWConsoleStyle.Small);
        public readonly Button Open = SWConsoleStyle.Action("Открыть на карте и назначить флот", SWConsoleStyle.Accent);
        public EventRow()
        {
            var box = Stack();
            Title.ClipText = true;
            Location.ClipText = true;
            Risk.ClipText = true;
            box.AddChild(Title);
            box.AddChild(Location);
            box.AddChild(Deadline);
            box.AddChild(Status);
            box.AddChild(Risk);
            box.AddChild(Open);
            Panel.AddChild(box);
        }
    }
}
