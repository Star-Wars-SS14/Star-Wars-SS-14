using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared._SW._Senate.Ministries;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._SW._Senate.Consoles.Statistics;

/// <summary>Server-published participants; the selection survives telemetry updates.</summary>
public sealed class SWStrategicBattlePanel : BoxContainer
{
    private IReadOnlyList<SWStrategicBattleView> _battles = Array.Empty<SWStrategicBattleView>();
    private int? _selected;
    private double _now;

    public SWStrategicBattlePanel()
    {
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        SeparationOverride = 4;
    }

    public void SetState(IReadOnlyList<SWStrategicBattleView>? battles, double now)
    {
        _battles = battles ?? Array.Empty<SWStrategicBattleView>();
        _now = now;
        if (_selected != null && !_battles.Any(b => b.EventId == _selected)) _selected = null;
        Refresh();
    }

    public void Open(int eventId)
    {
        if (!_battles.Any(b => b.EventId == eventId)) return;
        _selected = eventId;
        Refresh();
    }

    public void ClearSelection() { _selected = null; Refresh(); }

    private void Refresh()
    {
        RemoveAllChildren();
        if (_battles.Count == 0) return;
        var selected = _battles.FirstOrDefault(b => b.EventId == _selected);
        if (selected == null)
        {
            AddChild(SWConsoleStyle.Section("Столкновения"));
            foreach (var battle in _battles)
            {
                var id = battle.EventId;
                AddChild(SWConsoleStyle.Wrap($"{battle.Title} · {battle.Phase}", SWConsoleStyle.TextDim));
                var button = SWConsoleStyle.TabButton($"Состав боя {id}");
                button.OnPressed += _ => Open(id);
                AddChild(button);
            }
            return;
        }
        var close = SWConsoleStyle.TabButton("Закрыть состав боя");
        close.OnPressed += _ => { _selected = null; Refresh(); };
        AddChild(close);
        AddChild(SWConsoleStyle.Wrap($"{selected.Title}\nМесто: {selected.WorldName}\n{selected.Phase}", SWConsoleStyle.TextBright));
        AddFleets("Республика", selected.Republic);
        AddFleets("Другие силы", selected.Opponent);
        AddFleets("Подкрепления в пути", selected.Reinforcements);
    }

    private void AddFleets(string title, IReadOnlyList<SWStrategicBattleFleetView> fleets)
    {
        var count = fleets.Any(f => !f.HasCondition) ? "состав неизвестен" : fleets.Count.ToString();
        AddChild(SWConsoleStyle.Section($"{title}: {count}"));
        if (fleets.Count == 0) { AddChild(SWConsoleStyle.Wrap("Нет данных", SWConsoleStyle.TextDim)); return; }
        foreach (var fleet in fleets)
        {
            var eta = fleet.EtaHours is { } arrival ? $" · прибытие через {Math.Max(0, arrival - _now):0.0} ч" : string.Empty;
            var condition = fleet.HasCondition ? $" · урон {fleet.Damage:0.#} · снабжение {fleet.Supply:0.#}" : string.Empty;
            AddChild(SWConsoleStyle.Wrap($"{fleet.Name} · сила {fleet.Strength:0.#}{condition}{eta}", SWConsoleStyle.TextDim));
        }
    }
}
