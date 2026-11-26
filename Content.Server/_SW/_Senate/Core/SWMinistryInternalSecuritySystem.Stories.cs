using System.Linq;
using System.Text.RegularExpressions;
using Content.Shared._SW._Senate.Ministries;
using Content.Shared._SW._Senate.Sectors;

namespace Content.Server._SW._Senate.Core;

public sealed partial class SWMinistryInternalSecuritySystem
{
    private sealed class SecurityStory
    {
        public required SWSecurityCasePrototype Prototype;
        public required List<string> Worlds;
    }

    private readonly Dictionary<int, SecurityStory> _securityStories = new();
    private readonly Dictionary<SWSecurityCaseKind, string> _lastSecurityStory = new();

    private void ResetSecurityStories() { _securityStories.Clear(); _lastSecurityStory.Clear(); }
    private void RemoveSecurityStory(int id) => _securityStories.Remove(id);

    private bool InitializeSecurityStory(SWSecurityCaseView dossier, string title, bool directorCreated)
    {
        var active = _securityStories.Values.Select(s => s.Prototype.ID).ToHashSet();
        var candidates = _proto.EnumeratePrototypes<SWSecurityCasePrototype>()
            .Where(p => p.Kind == dossier.Kind && p.IsValid() && !active.Contains(p.ID))
            .OrderBy(p => p.ID).ToList();
        if (_lastSecurityStory.TryGetValue(dossier.Kind, out var last) && candidates.Count > 1
            && candidates.Any(p => p.ID != last))
            candidates.RemoveAll(p => p.ID == last);
        if (candidates.Count == 0) return false;

        var worlds = BindSecurityStoryWorlds(dossier.WorldId, dossier.Kind, 3);
        if (worlds == null) return false;
        var roll = _random.Next(candidates.Sum(p => p.Weight));
        var chosen = candidates[0];
        foreach (var candidate in candidates)
        {
            roll -= candidate.Weight;
            if (roll >= 0) continue;
            chosen = candidate; break;
        }
        var story = new SecurityStory { Prototype = chosen, Worlds = worlds };
        _securityStories[dossier.Id] = story;
        _lastSecurityStory[dossier.Kind] = chosen.ID;
        dossier.Title = directorCreated && !string.IsNullOrWhiteSpace(title) ? title : chosen.Title;
        dossier.RequiredEvidence = chosen.Stages.Count;
        dossier.Briefing = StoryText(story, chosen.Briefing, 0);
        SetSecurityStoryStage(dossier, story, 0);
        return true;
    }

    private List<string>? BindSecurityStoryWorlds(string start, SWSecurityCaseKind kind, int count)
    {
        var faction = kind == SWSecurityCaseKind.Imperial ? "empire" : "republic";
        var result = new List<string> { start };
        bool FindNext(bool allowRevisit, int maxHops)
        {
            if (result.Count == count) return true;
            var current = result[^1];
            var options = _securityWorlds.Values.Where(w => w.Faction == faction && w.Id != current && (allowRevisit || !result.Contains(w.Id)))
                .Select(w => (World: w, Path: SecurityPath(current, w.Id)))
                .Where(x => x.Path is { Count: > 0 } && x.Path.Count <= maxHops)
                .OrderBy(x => x.Path!.Count).ThenBy(x => x.World.Id).ToList();
            while (options.Count > 0)
            {
                // Randomize only equally short choices so isolated worlds use their nearest route.
                var shortest = options[0].Path!.Count;
                var tied = options.TakeWhile(x => x.Path!.Count == shortest).Count();
                var index = _random.Next(tied);
                var world = options[index].World; options.RemoveAt(index);
                result.Add(world.Id);
                if (FindNext(allowRevisit, maxHops)) return true;
                result.RemoveAt(result.Count - 1);
            }
            return false;
        }
        if (FindNext(false, 3) || FindNext(true, 3)) return result;
        // Isolated imperial worlds can be farther than three existing links from the next target.
        if (FindNext(false, int.MaxValue) || FindNext(true, int.MaxValue)) return result;
        return null;
    }

    private string StoryText(SecurityStory story, string text, int stage)
    {
        var world = _securityWorlds[story.Worlds[stage]];
        var sector = _proto.TryIndex<SWSectorPrototype>(world.SectorId, out var sectorProto) ? sectorProto.Name : "неизвестный сектор";
        text = text.Replace("{world}", world.Name).Replace("{originWorld}", _securityWorlds[story.Worlds[0]].Name)
            .Replace("{sector}", sector);
        if (stage + 1 < story.Worlds.Count)
            text = text.Replace("{nextWorld}", _securityWorlds[story.Worlds[stage + 1]].Name);
        return Regex.Replace(text, @"\{[^{}]+\}", "");
    }

    private void SetSecurityStoryStage(SWSecurityCaseView dossier, SecurityStory story, int stage)
    {
        dossier.WorldId = story.Worlds[stage];
        dossier.Scene = StoryText(story, story.Prototype.Stages[stage].Scene, stage);
        dossier.Objective = StoryText(story, story.Prototype.Stages[stage].Objective, stage);
    }

    private void AdvanceSecurityStory(SWSecurityCaseView dossier)
    {
        if (!_securityStories.TryGetValue(dossier.Id, out var story) || dossier.Evidence >= story.Prototype.Stages.Count) return;
        var stage = dossier.Evidence;
        dossier.Clues.Add(StoryText(story, story.Prototype.Stages[stage].Clue, stage));
        dossier.Evidence++;
        dossier.Status = dossier.Evidence >= dossier.RequiredEvidence ? "Улики собраны" : "Получена новая зацепка";
        if (dossier.Evidence < dossier.RequiredEvidence) SetSecurityStoryStage(dossier, story, dossier.Evidence);
        else dossier.Objective = "Выберите завершение операции";
    }

    private string SecurityStoryEnding(SWSecurityCaseView dossier, SWSecurityOrder order, bool success)
    {
        if (!_securityStories.TryGetValue(dossier.Id, out var story)) return dossier.Title;
        var text = !success ? story.Prototype.Failure : dossier.Kind == SWSecurityCaseKind.Domestic ? story.Prototype.Success
            : order == SWSecurityOrder.Sabotage ? story.Prototype.Sabotage : story.Prototype.Extraction;
        return $"{dossier.Title}: {StoryText(story, text, Math.Min(dossier.Evidence, story.Worlds.Count - 1))}";
    }
}
