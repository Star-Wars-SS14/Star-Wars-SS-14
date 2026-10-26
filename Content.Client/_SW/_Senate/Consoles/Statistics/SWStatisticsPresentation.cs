using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared._SW._Senate;
using Robust.Shared.Maths;

namespace Content.Client._SW._Senate.Consoles.Statistics;

public readonly record struct SWStatisticsPlotPoint(int Minute, float? Actual, float? Policy);

/// <summary>Presentation math uses prototype ranges and ids, never column order or invented history.</summary>
public static class SWStatisticsPresentation
{
    public static float? Normalize(float value, float min, float max) =>
        float.IsFinite(value) && float.IsFinite(min) && float.IsFinite(max) && max > min
            ? Math.Clamp((value - min) / (max - min), 0f, 1f) : null;

    public static string Level(float value, float min, float max) => Normalize(value, min, max) switch
    {
        null => "Нет данных",
        < 0.4f => "Низкий уровень",
        < 0.6f => "Средний уровень",
        _ => "Высокий уровень",
    };

    public static Color LevelColor(float value, float min, float max) => Normalize(value, min, max) switch
    {
        null => SWConsoleStyle.TextDim,
        < 0.4f => SWConsoleStyle.Bad,
        < 0.6f => SWConsoleStyle.Warn,
        _ => SWConsoleStyle.Good,
    };

    public static string Number(float value) => float.IsFinite(value) ? $"{value:0.#}" : "—";
    public static string Time(int minute) => $"Д{minute / 1440} {minute % 1440 / 60:00}:{minute % 60:00}";
    public static SWStatEntry? SectorMetric(SWSectorEntry sector, string id) => sector.Stats.FirstOrDefault(s => s.Id == id);

    public static List<SWStatisticsPlotPoint> Series(SWStatisticsConsoleState state, SWGlobalStatEntry metric, int period)
    {
        var points = state.History.Where(h => h.Minute <= state.SampleMinute &&
                (period == 0 || h.Minute >= state.SampleMinute - period))
            .GroupBy(h => h.Minute).Select(g => g.Last()).OrderBy(h => h.Minute)
            .Select(h =>
            {
                var value = h.Values.FirstOrDefault(v => v.Id == metric.Id);
                return new SWStatisticsPlotPoint(h.Minute,
                    value != null && float.IsFinite(value.Actual) ? value.Actual : null,
                    value != null && float.IsFinite(value.Policy) ? value.Policy : null);
            }).ToList();
        var current = new SWStatisticsPlotPoint(state.SampleMinute,
            float.IsFinite(metric.Actual) ? metric.Actual : null,
            float.IsFinite(metric.Policy) ? metric.Policy : null);
        if (points.Count > 0 && points[^1].Minute == current.Minute)
            points[^1] = current;
        else
            points.Add(current);
        return points;
    }
}
