using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using Content.Shared._SW._Senate;
using Robust.Shared.Maths;

namespace Content.Client._SW._Senate;

/// <summary>
/// Script of the Senate outro: which facts of the frozen round history are shown in which scene. Only the fields
/// listed here are public in the outro; everything else stays in the full round-end report.
/// </summary>
public sealed partial class SWSenateFinale
{
    /// <summary>Upper limit of the full outro; scenes without content are shortened or skipped.</summary>
    public const float MaxDuration = 56f;

    private const int MaxLaws = 2;
    private const int MaxListed = 3;
    private const int MaxSectors = 2;

    private enum Shot
    {
        Dusk,
        Chamber,
        Archive,
        Terminal,
        Closing,
    }

    /// <summary>One fact line. Text is always shown literally; the optional bar is drawn only from valid own bounds.</summary>
    private sealed record Row(string Text, Color Color, string? Tail = null, SWSenateHistoryStat? Bar = null, bool Large = false);

    private sealed record Page(string Title, string Note, Row[] Rows, string? More);

    private sealed record Scene(Shot Shot, float Start, float Length, Page[] Pages, Vector2 From, Vector2 To)
    {
        public float End => Start + Length;
    }

    private static readonly (string Id, string Name)[] GlobalStats =
    {
        ("SWGlobalEconomy", "Экономика"),
        ("SWGlobalSecurity", "Безопасность"),
        ("SWGlobalHealthcare", "Здравоохранение"),
    };

    private static Scene[] BuildScenes(SWSenateRoundHistory history, TimeSpan duration)
    {
        var scenes = new List<Scene>();
        var time = 0f;

        void Add(Shot shot, float length, Vector2 from, Vector2 to, params Page[] pages)
        {
            scenes.Add(new Scene(shot, time, length, pages, from, to));
            time += length;
        }

        var bright = SWConsoleStyle.TextBright;
        var dim = SWConsoleStyle.TextDim;

        // 1. Coruscant at dusk.
        var length = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        Add(Shot.Dusk, 6f, new Vector2(420, 195), new Vector2(540, 165),
            new Page("СОЗЫВ ЗАВЕРШЁН", "Корускант · итоговый снимок созыва", new[]
            {
                new Row(history.Days >= 0 ? $"Игровых дней: {history.Days}" : "Игровых дней: данные недоступны", bright, Large: true),
                new Row($"Длительность раунда: {(int) length.TotalHours:00}:{length.Minutes:00}:{length.Seconds:00}", dim),
            }, null));

        // 2. The empty rotunda: exactly the three stable global indicators.
        var statRows = new List<Row>();
        var anyStat = false;
        foreach (var (id, fallback) in GlobalStats)
        {
            var stat = history.Stats.FirstOrDefault(item => item.Id == id);
            var name = stat == null ? fallback : Clean(stat.Name, 40);
            if (name.Length == 0)
                name = fallback;
            if (stat == null || !float.IsFinite(stat.Start) || !float.IsFinite(stat.End))
            {
                statRows.Add(new Row($"{name}: данные недоступны", dim));
                continue;
            }

            anyStat = true;
            var delta = stat.End - stat.Start;
            statRows.Add(new Row($"{name}: {stat.Start:0.0} → {stat.End:0.0}", bright,
                $"   изменение {delta:+0.0;-0.0;0.0} ед.", HasValidBar(stat) ? stat : null));
        }

        Add(Shot.Chamber, anyStat ? 8f : 4f, new Vector2(540, 180), new Vector2(420, 180),
            new Page("ПОСЛЕ ЗАСЕДАНИЙ", "Значения показателей: начало → итог", statRows.ToArray(), null));

        // 3. Archive documents: counts and at most two decisions in the server's order.
        var lawRows = new List<Row>
        {
            new($"Принято законов: {history.PassedLaws} · поправок: {history.PassedAmendments}", bright),
        };
        foreach (var law in history.Laws.Take(MaxLaws))
        {
            var name = Clean(law.Name, 110);
            var tags = new List<string>();
            if (law.Amendment)
                tags.Add("поправка");
            if (law.Repealed)
                tags.Add("впоследствии отменён");
            lawRows.Add(new Row(name.Length == 0 ? "Название не указано" : $"«{name}»", bright,
                tags.Count > 0 ? " — " + string.Join(", ", tags) : null));
        }

        if (history.Laws.Length == 0 && history.PassedLaws <= 0 && history.PassedAmendments <= 0)
            lawRows.Add(new Row("Принятых решений в итоговом снимке не зафиксировано.", dim));
        var moreLaws = history.Laws.Length > MaxLaws || history.AdditionalLaws > 0 ? "Ещё решения — в полном отчёте" : null;
        var lawLength = history.Laws.Length > 0 ? 15f : history.PassedLaws > 0 || history.PassedAmendments > 0 ? 8f : 5f;
        Add(Shot.Archive, lawLength, new Vector2(480, 165), new Vector2(480, 195),
            new Page("РЕШЕНИЯ СОЗЫВА", "В итоговом снимке", lawRows.ToArray(), moreLaws));

        // 4. Sector terminal: verbatim samples; no ranking, no claims about what is absent.
        var pages = new List<Page>();
        var projects = history.Projects.Where(text => !string.IsNullOrWhiteSpace(text)).ToArray();
        if (projects.Length > 0)
        {
            pages.Add(new Page("К КОНЦУ СОЗЫВА", "В итоговом снимке · завершённые проекты",
                projects.Take(MaxListed).Select(text => new Row(Clean(text, 110), bright)).ToArray(),
                projects.Length > MaxListed ? "Ещё проекты — в полном отчёте" : null));
        }

        var events = history.Events.Where(text => !string.IsNullOrWhiteSpace(text)).ToArray();
        if (events.Length > 0)
        {
            pages.Add(new Page("К КОНЦУ СОЗЫВА", "В итоговом снимке · экономические события",
                events.Take(MaxListed).Select(text => new Row(Clean(text, 110), bright)).ToArray(),
                events.Length > MaxListed ? "Ещё события — в полном отчёте" : null));
        }

        var sectorRows = new List<Row>();
        var sectorsWithMore = false;
        foreach (var sector in history.Sectors)
        {
            if (sectorRows.Count >= MaxSectors)
                break;

            // Sector stats carry neither Id nor bounds: show the first finite pair verbatim, without bars.
            var stat = sector.Stats.FirstOrDefault(item => float.IsFinite(item.Start) && float.IsFinite(item.End));
            if (stat == null)
                continue;

            sectorsWithMore |= sector.Stats.Length > 1;
            var sectorName = Clean(sector.Name, 48);
            var statName = Clean(stat.Name, 40);
            sectorRows.Add(new Row($"{(sectorName.Length == 0 ? "Сектор" : sectorName)}: {(statName.Length == 0 ? "показатель" : statName)} {stat.Start:0.0} → {stat.End:0.0}", bright));
        }

        if (sectorRows.Count > 0)
        {
            pages.Add(new Page("К КОНЦУ СОЗЫВА", "Выборка по секторам · начало → итог", sectorRows.ToArray(),
                history.Sectors.Length > sectorRows.Count || sectorsWithMore ? "Все секторы — в полном отчёте" : null));
        }

        if (pages.Count > 0)
        {
            var terminalLength = pages.Count switch { 1 => 7f, 2 => 12f, _ => 18f };
            Add(Shot.Terminal, terminalLength, new Vector2(440, 180), new Vector2(520, 180), pages.ToArray());
        }

        // 5. Back to the archive as the lamp goes out. No people are listed: the history has no verified public
        // participant mark, so names stay out of the outro entirely; the full report remains one button away.
        Add(Shot.Closing, 9f, new Vector2(500, 186), new Vector2(480, 180),
            new Page("ИТОГИ СОЗЫВА", "Созыв окончен", new[]
            {
                new Row("Подробные итоги созыва — в «Полном отчёте».", bright, Large: true),
                new Row("Отчёт откроется по окончании хроники или по кнопке «Полный отчёт».", dim),
            }, null));

        return scenes.ToArray();
    }

    private static bool HasValidBar(SWSenateHistoryStat stat) =>
        float.IsFinite(stat.Min) && float.IsFinite(stat.Max) && stat.Max > stat.Min &&
        stat.Start >= stat.Min && stat.Start <= stat.Max && stat.End >= stat.Min && stat.End <= stat.Max;

    /// <summary>Longest run of characters without a break; longer runs are broken onto the next line.</summary>
    private const int MaxRun = 24;

    /// <summary>Combining marks kept after one base character; the rest are dropped so stacked marks stay in the line.</summary>
    private const int MaxMarks = 3;

    /// <summary>
    /// Prepares server text for literal display: control and format characters become spaces or are dropped, the
    /// text is cut to <paramref name="maxLength"/> characters with an ellipsis, and long unbroken runs are split.
    /// Works on <see cref="Rune"/>s (sandbox-allowed), so surrogate pairs are never split; combining marks stay with
    /// their base character and never start a new line. Invalid lone surrogates become U+FFFD.
    /// The result is still passed through FormattedMessage.AddText, never parsed as markup.
    /// </summary>
    private static string Clean(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var builder = new StringBuilder(Math.Min(text.Length, maxLength * 2));
        var run = 0;
        var count = 0;
        var marks = 0;
        var truncated = false;
        foreach (var rune in text.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
            {
                // A mark belongs to the previous character: kept only after a visible one, and only a few of them.
                if (run > 0 && marks < MaxMarks)
                {
                    builder.Append(rune.ToString());
                    marks++;
                }

                continue;
            }

            if (count >= maxLength)
            {
                truncated = true;
                break;
            }

            // Bidi overrides and other invisible format characters must not reorder or hide the line. They are dropped
            // without touching the mark counter, so they cannot be used to stack more marks on the same character.
            if (category == UnicodeCategory.Format)
                continue;

            if (Rune.IsWhiteSpace(rune) || category == UnicodeCategory.Control)
            {
                if (builder.Length > 0 && builder[builder.Length - 1] != ' ' && builder[builder.Length - 1] != '\n')
                    builder.Append(' ');
                run = 0;
                marks = 0;
                count++;
                continue;
            }

            if (run >= MaxRun)
            {
                builder.Append('\n');
                run = 0;
            }

            // The counter restarts only when a new base character is actually appended.
            builder.Append(rune.ToString());
            run++;
            marks = 0;
            count++;
        }

        var result = builder.ToString().Trim();
        return truncated && result.Length > 0 ? result + "…" : result;
    }
}
