using System;
using System.Linq;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._SW._Senate.Consoles.MinistryWork;

public sealed partial class SWHealthNetworkPanel
{
    private bool _showHealthLog;
    private void BuildCondition()
    {
        _condition.RemoveAllChildren();
        var world = _health.Worlds.FirstOrDefault(w => w.Id == SelectedWorldId);
        if (world == null) return;
        var infection = SWConsoleStyle.Row();
        var dynamics = Stack();
        var trend = !_health.DayActive ? "Ночная пауза" : world.Trend > .01f ? "Заражение растёт"
            : world.Trend < -.01f ? "Заражение снижается" : "Заражение стабильно";
        dynamics.AddChild(SWConsoleStyle.Wrap(trend, world.Trend > .01f && _health.DayActive ? SWConsoleStyle.Warn : SWConsoleStyle.TextBright));
        var rate = _health.DayActive ? world.Trend : 0;
        dynamics.AddChild(SWConsoleStyle.Wrap($"Заражение {world.Infection:0.#}%. Изменение: {rate:+0.##;-0.##;0} п.п./ч", SWConsoleStyle.TextBright));
        dynamics.AddChild(SWConsoleStyle.Bar(world.Infection, 0, 100, SWConsoleStyle.Bad));
        var suppression = SWConsoleStyle.Wrap($"Сдерживание заражения иммунитетом: -{world.ImmunitySuppressionPerHour:0.##} п.п./ч", SWConsoleStyle.Good);
        suppression.ToolTip = "Снижение местного роста и завоза инфекции благодаря текущему иммунитету. Итоговая динамика учитывает также медпомощь и остальные источники заражения.";
        dynamics.AddChild(suppression);
        infection.AddChild(dynamics);
        _condition.AddChild(infection);

        var protection = SWConsoleStyle.Row();
        var vaccination = Stack();
        vaccination.AddChild(SWConsoleStyle.Wrap("Вакцинация", SWConsoleStyle.Brass));
        vaccination.AddChild(SWConsoleStyle.Wrap(world.VaccinationStatus, world.ImmunityApplicationPerHour > 0 ? SWConsoleStyle.Good : SWConsoleStyle.TextDim));
        vaccination.AddChild(SWConsoleStyle.Wrap($"Иммунитет сейчас: {world.Immunity:0.#}%", SWConsoleStyle.TextBright));
        vaccination.AddChild(SWConsoleStyle.Bar(world.Immunity, 0, 100, SWConsoleStyle.Good));
        vaccination.AddChild(SWConsoleStyle.Wrap($"Доставленных доз осталось на +{world.PendingImmunity:0.##} п.п. иммунитета", SWConsoleStyle.TextBright));
        vaccination.AddChild(SWConsoleStyle.Wrap($"Прирост за час: до {world.ImmunityApplicationPerHour:0.##} п.п.", SWConsoleStyle.TextBright));
        var batch = SWConsoleStyle.Wrap($"Одна партия: до +{world.BatchCoverage:0.#} п.п. иммунитета", SWConsoleStyle.TextDim);
        batch.ToolTip = "При полной эффективности. Фактическая защита зависит от препарата и соответствия текущему штамму.";
        vaccination.AddChild(batch);
        vaccination.AddChild(SWConsoleStyle.Wrap($"Охват медпомощью {world.CareCoverage:0.#}%, клиниками {world.ClinicCoverage:0.#}%", SWConsoleStyle.TextDim));
        if (_health.Research.Stale) vaccination.AddChild(SWConsoleStyle.Wrap("Штамм изменился. Эффективность старого препарата снижена.", SWConsoleStyle.Warn));
        protection.AddChild(vaccination);
        _condition.AddChild(protection);

        var actions = new WrapContainer { HorizontalExpand = true, SeparationOverride = 6, CrossSeparationOverride = 4 };
        var clinics = SWConsoleStyle.Action("Клиники и помощь", SWConsoleStyle.Accent);
        clinics.OnPressed += _ => SelectSection(1);
        var supply = SWConsoleStyle.Action("Поставки", SWConsoleStyle.Accent);
        supply.OnPressed += _ => SelectSection(2);
        var research = SWConsoleStyle.Action("Исследование", SWConsoleStyle.Accent);
        research.OnPressed += _ => ResearchRequested?.Invoke();
        actions.AddChild(clinics); actions.AddChild(supply); actions.AddChild(research);
        _condition.AddChild(actions);
        var log = SWConsoleStyle.Action(_showHealthLog ? "Скрыть журнал" : "Журнал медсети", SWConsoleStyle.Brass);
        log.Name = "HealthNetworkLogToggle";
        log.OnPressed += _ => { _showHealthLog = !_showHealthLog; BuildCondition(); };
        _condition.AddChild(log);
        if (_showHealthLog)
        {
            foreach (var entry in _health.Log) _condition.AddChild(SWConsoleStyle.Wrap(entry, SWConsoleStyle.TextDim));
            if (_health.Log.Count == 0) _condition.AddChild(SWConsoleStyle.Wrap("Записей пока нет", SWConsoleStyle.TextDim));
        }
    }
}
