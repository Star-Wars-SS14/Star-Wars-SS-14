using System.Collections.Generic;
using Content.Shared._SW._Senate;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client._SW._Senate.Consoles.Statistics;

public static class SWStatisticsWidgets
{
    public static BoxContainer VerticalStack(int gap = 8) => new()
    {
        Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, SeparationOverride = gap,
    };

    public static Control Metric(string name, float value, float min = 0, float max = 100, Color? color = null, float? reference = null)
    {
        var box = VerticalStack(4);
        var head = new BoxContainer { HorizontalExpand = true };
        head.AddChild(SWConsoleStyle.Wrap(name, SWConsoleStyle.TextBright));
        head.AddChild(SWConsoleStyle.Text(SWStatisticsPresentation.Number(value), SWConsoleStyle.TextBright, SWConsoleStyle.Strong));
        box.AddChild(head);
        box.AddChild(new SWStatisticsBar { Value = value, Minimum = min, Maximum = max, Fill = color ?? SWConsoleStyle.Accent, Reference = reference });
        box.ToolTip = $"{name}: {SWStatisticsPresentation.Number(value)}; шкала {min:0.#}–{max:0.#}";
        return box;
    }

    public static void StatEffects(BoxContainer target, IReadOnlyList<SWBillEffectEntry> effects, bool perHour = false)
    {
        if (effects.Count == 0) return;
        target.AddChild(SWConsoleStyle.Section(perHour ? "Влияние режима за игровой час" : "Последствия"));
        foreach (var effect in effects)
            target.AddChild(SWConsoleStyle.Wrap($"{effect.StatName}   {effect.Delta:+0.#;-0.#;0}{(perHour || effect.PerHour ? "/ч" : string.Empty)}", effect.Color));
    }

    public static Control Operations(SWStatisticsConsoleState s, int section)
    {
        var b = VerticalStack(14);
        void Text(string value) { if (!string.IsNullOrWhiteSpace(value)) b.AddChild(SWConsoleStyle.Wrap(value, SWConsoleStyle.TextBright)); }
        void Risk(string label, int value, string status = "")
        {
            b.AddChild(Metric(label, value, color: SWConsoleStyle.SeverityColor(value)));
            if (status.Length > 0) b.AddChild(SWConsoleStyle.Wrap(status, SWConsoleStyle.TextDim));
        }
        switch (section)
        {
            case 0:
                Text(s.DefconName.Length > 0 ? $"DEFCON {s.DefconLevel}, {s.DefconName}" : "Режим боеготовности не указан");
                Risk("Давление на границы", s.BorderPressure, s.BorderStatus);
                StatEffects(b, s.DefconEffects);
                break;
            case 1:
                Text(s.PostureName);
                Risk("Внутренняя угроза", s.Threat, s.ThreatStatus);
                Risk("Общественный накал", s.Unrest, s.UnrestStatus);
                StatEffects(b, s.PostureEffects, true);
                break;
            case 2:
                Text(s.PandemicStatus);
                b.AddChild(Metric("Иммунитет", s.Immunity));
                if (s.PathogenActive)
                {
                    Risk("Среднее заражение", s.InfectionAvg);
                    Text($"Патоген: {s.PathogenName}, штамм {s.PathogenStrain}");
                    b.AddChild(Metric("Исследование вакцины, %", s.Research));
                }
                break;
            case 3:
                Text(s.EconPhaseName);
                Text($"Рост: {s.Growth:+0.0;-0.0;0} за игровой час");
                Risk("Перегрев экономики", s.Overheat, s.OverheatStatus);
                break;
            case 4:
                Text($"Кредитный рейтинг: {(s.CreditRating.Length > 0 ? s.CreditRating : "нет данных")}");
                Text($"Ключевая ставка: {s.KeyRate}%");
                Risk("Индекс инфляции", s.Inflation, s.InflationStatus);
                Risk("Долговая нагрузка", s.Debt);
                break;
            case 5:
                Text(s.JusticeStanceName);
                Risk("Нагрузка судов", s.Backlog, s.BacklogStatus);
                Text($"Дел в производстве: {s.CaseCount}");
                Risk("Индекс коррупции", s.CorruptionIndex, s.CorruptionStatus);
                break;
            case 6:
                if (!s.PowersActive) { Text("Чрезвычайные полномочия не действуют."); break; }
                Text(s.EmergencyLevelName);
                Risk("Тирания", s.Authority);
                b.AddChild(Metric("Легитимность", s.Legitimacy));
                break;
        }
        return b;
    }
}
