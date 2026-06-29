using System.Linq;
using Content.Server.GameTicking;
using Content.Server._SW._Senate.Economy;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Ambitions;
using Content.Shared._SW._Senate.Sectors;
using Content.Shared.GameTicking;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._SW._Senate.Core;

public sealed class SWSenateAmbitionSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SWSenateStateSystem _senate = default!;
    [Dependency] private readonly SWSenatePartySystem _party = default!;
    [Dependency] private readonly SWEmergencyPowersSystem _powers = default!;

    private const float UpdateInterval = 5f;

    // Assigned once per round per owner (sector id for senators, "republic" for the Chancellor); persists
    // across game days and is only wiped on round restart.
    private readonly Dictionary<string, List<SWActiveAmbition>> _assigned = new();
    private float _accumulator;

    public override void Initialize()
    {
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        SubscribeLocalEvent<RoundEndTextAppendEvent>(OnRoundEnd);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _assigned.Clear();
    }

    public override void Update(float frameTime)
    {
        _accumulator += frameTime;
        if (_accumulator < UpdateInterval)
            return;

        _accumulator = 0f;

        foreach (var (sectorId, _) in _senate.SectorHolders)
            EnsureAssigned(sectorId, SWAmbitionRole.Senator, sectorId);

        if (_senate.Chancellor != null)
            EnsureAssigned(SWSenateDilemmaSystem.RepublicOwner, SWAmbitionRole.Chancellor, null);
    }

    private void EnsureAssigned(string owner, SWAmbitionRole role, string? sectorId)
    {
        if (_assigned.ContainsKey(owner))
            return;

        var pool = _proto.EnumeratePrototypes<SWAmbitionPrototype>().Where(a => a.Role == role).ToList();
        _random.Shuffle(pool);

        var target = Math.Min(_random.Next(2, 4), pool.Count);
        var list = new List<SWActiveAmbition>();

        foreach (var proto in pool)
        {
            if (list.Count >= target)
                break;

            string? rival = null;
            if (proto.Kind == SWAmbitionKind.OutdoSector)
            {
                rival = PickRivalSector(sectorId);
                if (rival == null)
                    continue;
            }

            list.Add(new SWActiveAmbition { AmbitionId = proto.ID, Owner = owner, RivalSector = rival });
        }

        _assigned[owner] = list;
    }

    private string? PickRivalSector(string? self)
    {
        var others = _senate.SectorGlobals.Keys.Where(id => id != self).ToList();
        return others.Count == 0 ? null : _random.Pick(others);
    }

    private void EnsureForOwner(string owner)
    {
        if (owner == SWSenateDilemmaSystem.RepublicOwner)
            EnsureAssigned(owner, SWAmbitionRole.Chancellor, null);
        else
            EnsureAssigned(owner, SWAmbitionRole.Senator, owner);
    }

    public List<SWAmbitionEntry> BuildAmbitions(string owner)
    {
        EnsureForOwner(owner);

        var list = new List<SWAmbitionEntry>();
        if (!_assigned.TryGetValue(owner, out var ambitions))
            return list;

        foreach (var active in ambitions)
        {
            if (!_proto.TryIndex<SWAmbitionPrototype>(active.AmbitionId, out var proto))
                continue;

            var (met, progress) = Evaluate(active, proto);
            list.Add(new SWAmbitionEntry
            {
                Name = proto.Name,
                Description = Describe(proto, active),
                Met = met,
                Progress = progress,
            });
        }

        return list;
    }

    private string Describe(SWAmbitionPrototype proto, SWActiveAmbition active)
    {
        if (proto.Kind == SWAmbitionKind.OutdoSector && active.RivalSector != null)
            return $"{proto.Description} (соперник: {SectorName(active.RivalSector)})";

        return proto.Description;
    }

    private (bool met, string progress) Evaluate(SWActiveAmbition active, SWAmbitionPrototype proto)
    {
        switch (proto.Kind)
        {
            case SWAmbitionKind.SectorStat:
            {
                var value = SectorGlobal(active.Owner, proto.Stat);
                return (Compare(value, proto.Comparison, proto.Threshold), StatProgress(value, proto));
            }
            case SWAmbitionKind.RepublicStat:
            {
                var value = RepublicGlobal(proto.Stat);
                return (Compare(value, proto.Comparison, proto.Threshold), StatProgress(value, proto));
            }
            case SWAmbitionKind.OutdoSector:
            {
                var mine = SectorGlobal(active.Owner, proto.Stat);
                var theirs = active.RivalSector != null ? SectorGlobal(active.RivalSector, proto.Stat) : 0f;
                return (mine >= theirs + proto.Margin, $"{mine:0} против {theirs:0} (нужно +{proto.Margin:0})");
            }
            case SWAmbitionKind.AuthorLawStat:
            {
                var holder = OwnerHolder(active.Owner);
                var met = holder != null && _senate.Bills.Any(b =>
                    b.Stage == SWBillStage.Passed && b.Author == holder
                    && b.Effects.TryGetValue(proto.Stat, out var d) && d > 0);
                return (met, met ? "закон принят" : "закона нет");
            }
            case SWAmbitionKind.PassOwnLaws:
            {
                var holder = OwnerHolder(active.Owner);
                var n = holder == null ? 0 : _senate.Bills.Count(b => b.Stage == SWBillStage.Passed && b.Author == holder);
                return (n >= proto.Count, $"{n}/{proto.Count}");
            }
            case SWAmbitionKind.PassBills:
            {
                var n = _senate.Bills.Count(b => b.Stage == SWBillStage.Passed);
                return (n >= proto.Count, $"{n}/{proto.Count}");
            }
            case SWAmbitionKind.PartyStrong:
            {
                var info = _party.GetMembership(active.Owner);
                if (info is not { Formed: true } party)
                    return (false, "нет сформированной партии");
                var need = (int) proto.Threshold;
                return (party.Cohesion >= need, $"сплочённость {party.Cohesion:+0;-0;0} / нужно +{need}");
            }
            case SWAmbitionKind.PartySize:
            {
                var info = _party.GetMembership(active.Owner);
                var required = Math.Max(3, (int) Math.Ceiling(_senate.SectorHolders.Count * proto.Margin));
                if (info is not { Formed: true } party)
                    return (false, $"нет партии / нужно {required}");
                return (party.Members >= required, $"{party.Members}/{required}");
            }
            case SWAmbitionKind.Consolidation:
            {
                var need = (int) proto.Threshold;
                return (_powers.Authority >= need, $"авторитет {_powers.Authority} / нужно {need}");
            }
            default:
                return (false, string.Empty);
        }
    }

    private static string StatProgress(float value, SWAmbitionPrototype proto)
    {
        var sign = proto.Comparison == SWAmbitionComparison.Above ? ">" : "<";
        return $"{value:0} / нужно {sign}{proto.Threshold:0}";
    }

    private void OnRoundEnd(RoundEndTextAppendEvent args)
    {
        // Make sure everyone holding a seat at round end is scored, even if they never opened the console.
        foreach (var (sectorId, _) in _senate.SectorHolders)
            EnsureAssigned(sectorId, SWAmbitionRole.Senator, sectorId);
        if (_senate.Chancellor != null)
            EnsureAssigned(SWSenateDilemmaSystem.RepublicOwner, SWAmbitionRole.Chancellor, null);

        if (_assigned.Count == 0)
            return;

        var standings = new List<(string Label, int Score, int Total, List<string> Lines)>();
        foreach (var (owner, ambitions) in _assigned)
        {
            if (ambitions.Count == 0)
                continue;

            var holder = OwnerHolder(owner) ?? "—";
            var label = owner == SWSenateDilemmaSystem.RepublicOwner
                ? $"Канцлер ({holder})"
                : $"{SectorName(owner)} — {holder}";

            var score = 0;
            var lines = new List<string>();
            foreach (var active in ambitions)
            {
                if (!_proto.TryIndex<SWAmbitionPrototype>(active.AmbitionId, out var proto))
                    continue;

                var (met, progress) = Evaluate(active, proto);
                if (met)
                    score++;

                var mark = met ? "[color=#2ECC71]выполнено[/color]" : "[color=gray]провалено[/color]";
                lines.Add($"    - {proto.Name}: {mark} ({progress})");
            }

            standings.Add((label, score, ambitions.Count, lines));
        }

        if (standings.Count == 0)
            return;

        standings.Sort((a, b) => b.Score.CompareTo(a.Score));
        var best = standings.Max(s => s.Score);

        args.AddLine("[bold]Амбиции Сената:[/bold]");
        if (best > 0)
        {
            var winners = standings.Where(s => s.Score == best).Select(s => s.Label);
            args.AddLine($"[color=gold]Победители ({best} очк.): {string.Join(", ", winners)}[/color]");
        }
        else
        {
            args.AddLine("[color=gray]Никто не выполнил ни одной амбиции.[/color]");
        }

        foreach (var standing in standings)
        {
            args.AddLine($"[bold]{standing.Label}[/bold] — {standing.Score}/{standing.Total} очк.");
            foreach (var line in standing.Lines)
                args.AddLine(line);
        }
    }

    private float SectorGlobal(string sectorId, string stat) =>
        _senate.SectorGlobals.TryGetValue(sectorId, out var globals) && globals.TryGetValue(stat, out var value)
            ? value
            : 50f;

    private float RepublicGlobal(string stat) =>
        _senate.GetRepublicGlobals().TryGetValue(stat, out var value) ? value : 50f;

    private string? OwnerHolder(string owner) =>
        owner == SWSenateDilemmaSystem.RepublicOwner ? _senate.Chancellor : _senate.GetSectorHolder(owner);

    private string SectorName(string id) =>
        _proto.TryIndex<SWSectorPrototype>(id, out var proto) ? proto.Name : id;

    private static bool Compare(float value, SWAmbitionComparison comparison, float threshold) =>
        comparison == SWAmbitionComparison.Above ? value > threshold : value < threshold;
}

public sealed class SWActiveAmbition
{
    public string AmbitionId = string.Empty;
    public string Owner = string.Empty;
    public string? RivalSector;
}
