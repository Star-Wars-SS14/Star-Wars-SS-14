using System;
using System.Collections.Generic;
using System.Linq;
using Robust.Shared.Localization;

namespace Content.Client._SW._Senate.Consoles.Work;

public sealed class SWBillTextEffect
{
    public string StatId = string.Empty;
    public string StatName = string.Empty;
    public string GlobalId = string.Empty;
    public string GlobalName = string.Empty;
    public int Delta;
}

public sealed class SWBillTextSuggestion
{
    public string Title = string.Empty;
    public string Description = string.Empty;
}

public static class SWBillTextGenerator
{
    private static readonly HashSet<string> Known = new()
    {
        "SWStatMedicine",
        "SWStatEpidemic",
        "SWStatWelfare",
        "SWStatCoverage",
        "SWStatFleet",
        "SWStatArmy",
        "SWStatLogistics",
        "SWStatReserve",
        "SWStatSchools",
        "SWStatAcademies",
        "SWStatResearch",
        "SWStatTech",
        "SWStatTreasury",
        "SWStatTaxes",
        "SWStatTrade",
        "SWStatEmployment",
        "SWStatPolice",
        "SWStatIntel",
        "SWStatCensorship",
        "SWStatCounter",
        "SWStatManufacturing",
        "SWStatEnergy",
        "SWStatMining",
        "SWStatTransport",
        "SWStatEmpireBorder",
        "SWStatNeutral",
        "SWStatTreaties",
        "SWStatLiving",
        "SWStatRights",
        "SWStatMorale",
        "SWStatCrime",
    };

    public static List<SWBillTextSuggestion> Generate(IReadOnlyList<SWBillTextEffect> effects, string? amendedLawName = null)
    {
        var order = Comparer<string>.Create(string.CompareOrdinal);
        var ranked = effects.Where(e => e.Delta != 0)
            .OrderByDescending(e => Math.Abs((long) e.Delta))
            .ThenBy(e => e.GlobalId, order).ThenBy(e => e.StatId, order)
            .ThenBy(e => e.StatName, order).ThenBy(e => e.Delta).ToList();
        if (ranked.Count == 0)
            return new List<SWBillTextSuggestion>();

        // GroupBy preserves the order of the first, highest-weight effect in each sphere.
        var groups = ranked.GroupBy(e => e.GlobalId).ToList();
        var broad = ranked.Count > 2 && Math.Abs((long) ranked[0].Delta) == Math.Abs((long) ranked[2].Delta)
            && ranked.TakeWhile(e => Math.Abs((long) e.Delta) == Math.Abs((long) ranked[0].Delta))
                .Select(e => e.GlobalId).Distinct().Count() >= 3;
        var top = ranked.Take(2).ToList();
        var topics = string.Join(" и ", top.Select(e => Form(e, "topic")));
        var actions = string.Join(" и ", top.Select(e => Form(e, e.Delta > 0 ? "positive-title" : "negative-title")));
        var firstWord = actions.Length > 0 ? actions[0] : 'п';
        var preposition = "аеёиоуыэюя".Contains(firstWord) ? "Об " : "О ";
        var amendment = !string.IsNullOrWhiteSpace(amendedLawName);
        var titles = amendment
            ? new[] { $"О поправках к закону «{amendedLawName}»", $"Об изменениях закона «{amendedLawName}»", $"О пересмотре закона «{amendedLawName}»" }
            : broad
                ? new[] { "О пересмотре приоритетов Республики", "Об изменении направлений политики Республики", "О приоритетах государственной политики" }
                : new[] { preposition + actions, "О приоритетах " + topics, "О пересмотре политики в области " + topics };

        var sentences = groups.Select(g => Capitalize(string.Join("; ", g.Select(Phrase))) + ".").ToList();
        var first = string.Join(" ", sentences);
        var nouns = groups.Select(g => string.Join("; ", g.Select(Noun))).ToList();
        var second = "Законопроект предусматривает " + string.Join(". Также предусматривается ", nouns) + ".";
        var third = string.Join("\n\n", groups.Select((g, i) =>
            $"Статья {i + 1}. {Heading(g)}\n{Capitalize(string.Join("; ", g.Select(Phrase)))}."));
        if (amendment)
        {
            first = "Поправки к действующему закону. " + first;
            second = "Поправки к действующему закону предусматривают " + string.Join(". Также предусматривается ", nouns) + ".";
            third = "Поправки к действующему закону.\n\n" + third;
        }
        return new List<SWBillTextSuggestion>
        {
            new() { Title = titles[0], Description = first },
            new() { Title = titles[1], Description = second },
            new() { Title = titles[2], Description = third },
        };
    }

    private static string Form(SWBillTextEffect effect, string form)
    {
        if (Known.Contains(effect.StatId))
        {
            var id = effect.StatId.Substring("SWStat".Length).ToLowerInvariant();
            return Loc.GetString($"sw-bill-text-{id}-{form}");
        }
        var name = string.IsNullOrWhiteSpace(effect.StatName) ? "показатель политики" : effect.StatName;
        return form switch
        {
            "heading" => name,
            "topic" => $"направления «{name}»",
            "positive-title" => $"усилении направления «{name}»",
            "negative-title" => $"ослаблении направления «{name}»",
            "positive-noun" => $"усиление направления «{name}»",
            "negative-noun" => $"ослабление направления «{name}»",
            "positive" => $"усилить направление «{name}»",
            _ => $"ослабить направление «{name}»",
        };
    }

    private static string Heading(IEnumerable<SWBillTextEffect> group)
    {
        return string.Join(" и ", group.Select(e => Form(e, "heading")));
    }

    private static string Phrase(SWBillTextEffect effect)
    {
        var phrase = Form(effect, effect.Delta > 0 ? "positive" : "negative");
        return Math.Abs((long) effect.Delta) >= 6 ? "существенно " + phrase : phrase;
    }

    private static string Noun(SWBillTextEffect effect)
    {
        var phrase = Form(effect, effect.Delta > 0 ? "positive-noun" : "negative-noun");
        return Math.Abs((long) effect.Delta) >= 6 ? "существенное " + phrase : phrase;
    }

    private static string Capitalize(string text)
    {
        return text.Length == 0 ? text : text.Substring(0, 1).ToUpperInvariant() + text.Substring(1);
    }
}


