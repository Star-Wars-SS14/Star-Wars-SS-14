using System;
using System.Linq;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Ministries;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._SW._Senate.Consoles.MinistryWork;

public sealed partial class SWMinistryWorkConsoleMenu
{
    private static Control MakeStanceEffects(System.Collections.Generic.List<SWBillEffectEntry> effects)
    {
        var row = new WrapContainer { HorizontalExpand = true, SeparationOverride = 8, CrossSeparationOverride = 2 };
        foreach (var effect in effects)
            row.AddChild(SWConsoleStyle.Text($"{effect.StatName} {effect.Delta:+0.#;-0.#;0}{(effect.PerHour ? "/ч" : "")}", effect.Color, SWConsoleStyle.Small));
        return row;
    }

    private void BuildPlanetaryDefense(SWMinistryWorkConsoleState state)
    {
        if (!state.IsDefense) return;
        if (_defensePlanetary == null)
        {
            _defensePlanetary = new SWDefensePlanetaryPanel();
            _defensePlanetary.ProgramRequested += _owner.BuildWorldDefense;
            _defensePlanetary.ResupplyRequested += _owner.ResupplyWorldDefense;
            _defensePlanetary.WorldMapRequested += id =>
            {
                ShowTab(FleetTab);
                _defenseMap?.SelectWorld(id);
                _defenseMap?.Map.SelectWorld(id, true);
            };
            PlanetaryDefensePanel.AddChild(_defensePlanetary);
        }
        var canAct = state.HasCard && state.IsMinisterCard && state.HasMinistry
            && state.DefenseMap is { ClockRunning: true, MinisterActive: true };
        _defensePlanetary.SetState(state.DefensePlanetary, canAct);
    }

    private void UpdateDefenseHeader(SWMinistryWorkConsoleState state)
    {
        DefenseHeaderBudgetPanel.Visible = (state.IsDefense || state.IsEconomy) && state.HasMinistry;
        // Header figures change on every publication, independently of command card caching
        // and whichever ministry tab the player currently has open.
        DefenseHeaderBudget.Text = $"Бюджет: {state.MinistryBudget:N0} кр";
        DefenseHeaderUpkeep.Visible = state.IsDefense;
        DefenseHeaderUpkeep.Text = $"Содержание: {state.DefenseMap?.UpkeepPerHour ?? state.FleetUpkeep:N0} кр/ч";
        DefenseHeaderBudgetPhase.Visible = !string.IsNullOrWhiteSpace(state.BudgetPhaseLabel);
        DefenseHeaderBudgetPhase.Text = $"Бюджет дня: {state.BudgetPhaseLabel}";
    }

    private void BuildFleet(SWMinistryWorkConsoleState state)
    {
        if (!state.IsDefense) return;
        if (_defenseMap == null)
        {
            _defenseMap = new SWDefenseMapPanel();
            _defenseMap.OrderRequested += _owner.OrderFleet;
            _defenseMap.RecruitRequested += _owner.RecruitFleet;
            FleetContent.AddChild(_defenseMap);
        }
        _defenseMap.SetState(state.DefenseMap);
    }

    private void BuildOps(SWMinistryWorkConsoleState state)
    {
        if (!state.IsDefense) return;
        if (_defenseOperations == null)
        {
            _defenseOperations = new SWDefenseOperationsPanel();
            _defenseOperations.EventSelected += id =>
            {
                _defenseMap?.SelectEvent(id);
                ShowTab(FleetTab);
            };
            OpsContent.AddChild(_defenseOperations);
        }
        _defenseOperations.SetState(state.DefenseMap, state.DefenseLog);
    }

    private void BuildCommand(SWMinistryWorkConsoleState state)
    {
        if (!state.IsDefense) return;
        var revision = $"{state.Stance}:{state.DefconLevel}:{state.StanceName}:{state.WarPending}:" +
            $"{state.HasCard}:{state.IsMinisterCard}:{state.HasMinistry}:{state.DefenseMap?.ClockRunning}:{state.DefenseMap?.MinisterActive}:" +
            $"{state.PendingStanceName}:{state.BorderPressure}:{state.BorderStatus}:{state.WarWeariness}:" +
            string.Join(";", state.StanceEffects.Select(e => $"{e.StatName}:{e.Delta}:{e.Color}:{e.PerHour}"));
        if (_defenseCommandRevision == revision) return;
        _defenseCommandRevision = revision;
        CommandContent.RemoveAllChildren();

        var card = SWConsoleStyle.Row();
        var readiness = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 6 };
        readiness.AddChild(SWConsoleStyle.Text("СТРАТЕГИЧЕСКОЕ КОМАНДОВАНИЕ", SWConsoleStyle.Brass, SWConsoleStyle.Small));
        readiness.AddChild(SWConsoleStyle.Text($"DEFCON {state.DefconLevel}   {state.StanceName}", state.StanceColor, SWConsoleStyle.Title));
        if (state.StanceEffects.Count > 0) readiness.AddChild(MakeStanceEffects(state.StanceEffects));
        if (state.WarPending)
            readiness.AddChild(SWConsoleStyle.Wrap($"Ожидает одобрения Канцлера: {state.PendingStanceName}", SWConsoleStyle.Warn));
        readiness.AddChild(SWConsoleStyle.Wrap("Повышение до DEFCON 1–2 — через Канцлера.", SWConsoleStyle.TextDim));
        card.AddChild(readiness);
        CommandContent.AddChild(card);

        var ladder = new BoxContainer { Name = "DefenseDefconControl", HorizontalExpand = true, MaxWidth = 560,
            HorizontalAlignment = Robust.Client.UserInterface.Control.HAlignment.Left,
            Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
        var canChange = state.HasCard && state.IsMinisterCard && state.HasMinistry
            && state.DefenseMap is { ClockRunning: true, MinisterActive: true };
        foreach (var stance in new[] { SWDefenseStance.Peace, SWDefenseStance.Vigilance, SWDefenseStance.Alert, SWDefenseStance.Mobilization, SWDefenseStance.War })
        {
            var active = stance == state.Stance;
            var level = 5 - (int) stance;
            var button = new SWConsoleButton
            {
                Name = $"DefenseDefcon{level}", Text = level.ToString(), HorizontalExpand = true,
                MinWidth = 60, MaxWidth = 104,
                ToggleMode = true, Pressed = active, Disabled = active || !canChange,
            };
            button.SetAppearance(StanceClientColor(stance), selected: active);
            button.RemoveAllChildren();
            var labels = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical,
                HorizontalAlignment = Robust.Client.UserInterface.Control.HAlignment.Center, SeparationOverride = 0 };
            labels.AddChild(SWConsoleStyle.Text(level.ToString(), active ? SWConsoleStyle.TextBright : StanceClientColor(stance),
                SWConsoleStyle.Title, align: Robust.Client.UserInterface.Control.HAlignment.Center));
            var caption = stance switch { SWDefenseStance.Vigilance => "Готовность", SWDefenseStance.Mobilization => "Сбор сил", _ => StanceClientName(stance) };
            labels.AddChild(SWConsoleStyle.Text(caption, active ? SWConsoleStyle.TextBright : SWConsoleStyle.TextDim,
                SWConsoleStyle.Small, align: Robust.Client.UserInterface.Control.HAlignment.Center));
            button.AddChild(labels);
            var approval = stance >= SWDefenseStance.Mobilization && stance > state.Stance;
            button.ToolTip = !canChange ? "Штаб закрыт или нет карты министра"
                : active ? "Действующий уровень"
                : approval ? $"DEFCON {level}: запрос Канцлеру" : $"DEFCON {level}: установить";
            button.OnPressed += _ => { button.Pressed = active; _owner.SetStance(stance); };
            ladder.AddChild(button);
        }
        CommandContent.AddChild(ladder);
        CommandContent.AddChild(SWConsoleStyle.Section("Давление на границе"));
        CommandContent.AddChild(SWConsoleStyle.Text($"{state.BorderStatus}   {state.BorderPressure}/100", BorderColor(state.BorderPressure), SWConsoleStyle.Strong));
        CommandContent.AddChild(SWConsoleStyle.Bar(state.BorderPressure, 0, 100, BorderColor(state.BorderPressure)));
        CommandContent.AddChild(SWConsoleStyle.Section("Усталость от войны"));
        CommandContent.AddChild(SWConsoleStyle.Text($"{state.WarWeariness}/100", SWConsoleStyle.TextBright, SWConsoleStyle.Strong));
        CommandContent.AddChild(SWConsoleStyle.Bar(state.WarWeariness, 0, 100, state.WarWeariness >= 60 ? SWConsoleStyle.Bad : SWConsoleStyle.Warn));
    }
}
