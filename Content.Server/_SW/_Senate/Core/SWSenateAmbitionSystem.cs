using System.Linq;
using Content.Server.GameTicking;
using Content.Server._SW._Senate.Antag;
using Content.Server._SW._Senate.Economy;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Ambitions;
using Content.Shared._SW._Senate.Ministries;
using Content.Shared._SW._Senate.Sectors;
using Content.Shared.GameTicking;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._SW._Senate.Core;

public sealed class SWSenateAmbitionSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly SWSenateStateSystem _senate = default!;
    [Dependency] private readonly SWSenatePartySystem _party = default!;
    [Dependency] private readonly SWEmergencyPowersSystem _powers = default!;
    [Dependency] private readonly SWMinistrySystem _ministry = default!;
    [Dependency] private readonly SWMinistryDefenseSystem _defense = default!;
    [Dependency] private readonly SWMinistryInternalSecuritySystem _security = default!;
    [Dependency] private readonly SWMinistryEconomySystem _economy = default!;
    [Dependency] private readonly SWMinistryFinanceSystem _finance = default!;
    [Dependency] private readonly SWMinistryJusticeSystem _justice = default!;
    [Dependency] private readonly SWMinistryHealthSystem _health = default!;
    [Dependency] private readonly SWDeptHealthcareSystem _deptHealth = default!;
    [Dependency] private readonly SWDeptGuardSystem _deptGuard = default!;
    [Dependency] private readonly SWDeptArbitrageSystem _deptArbitrage = default!;
    [Dependency] private readonly SWDeptExpenditureSystem _deptExpenditure = default!;
    [Dependency] private readonly SWBureauShipsSystem _bureauShips = default!;
    [Dependency] private readonly SWBureauWeaponsSystem _bureauWeapons = default!;
    [Dependency] private readonly SWBureauTransitSystem _bureauTransit = default!;
    [Dependency] private readonly SWSenateCourtSystem _court = default!;
    [Dependency] private readonly SWMercSystem _merc = default!;
    [Dependency] private readonly SWSenateCommitteeSystem _committee = default!;

    private const float UpdateInterval = 5f;

    // Assigned once per round per owner (sector id for senators, "republic" for the Chancellor, the
    // ministry/department/bureau id for a minister-role office); persists across game days and is only
    // wiped on round restart, and stays with the OFFICE even if its holder changes.
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
        if (!_time.ModeActive)
            return;

        _accumulator += frameTime;
        if (_accumulator < UpdateInterval)
            return;

        _accumulator = 0f;

        foreach (var (sectorId, _) in _senate.SectorHolders)
            EnsureAssigned(sectorId, SWAmbitionRole.Senator, sectorId, null);

        if (_senate.Chancellor != null)
            EnsureAssigned(SWSenateDilemmaSystem.RepublicOwner, SWAmbitionRole.Chancellor, null, null);

        foreach (var (ministryId, _) in _ministry.Holders)
            EnsureAssigned(ministryId, SWAmbitionRole.Minister, null, ministryId);

        EnsureCourtAssigned();
    }

    private void EnsureCourtAssigned()
    {
        if (_court.Chief != null)
            EnsureAssigned("court-chief", SWAmbitionRole.Judicial, null, null, "chief");
        for (var i = 0; i < _court.Judges.Count; i++)
            EnsureAssigned($"court-judge-{i}", SWAmbitionRole.Judicial, null, null, "judge");
        if (_court.Prosecutor != null)
            EnsureAssigned("court-prosecutor", SWAmbitionRole.Judicial, null, null, "prosecutor");
        if (_court.Defender != null)
            EnsureAssigned("court-defender", SWAmbitionRole.Judicial, null, null, "defender");
    }

    private void EnsureAssigned(string owner, SWAmbitionRole role, string? sectorId, string? ministryId, string? courtRole = null)
    {
        if (_assigned.ContainsKey(owner))
            return;

        var pool = _proto.EnumeratePrototypes<SWAmbitionPrototype>()
            .Where(a => a.Role == role
                && (role != SWAmbitionRole.Minister || a.MinistryId == ministryId)
                && (role != SWAmbitionRole.Judicial || a.CourtRole == courtRole))
            .ToList();
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
            EnsureAssigned(owner, SWAmbitionRole.Chancellor, null, null);
        else if (owner.StartsWith("court-"))
            EnsureCourtAssigned();
        else if (_proto.HasIndex<SWMinistryPrototype>(owner))
            EnsureAssigned(owner, SWAmbitionRole.Minister, null, owner);
        else
            EnsureAssigned(owner, SWAmbitionRole.Senator, owner, null);
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
                    b.Stage == SWBillStage.Passed && b.SpecialMotionType == null && b.Author == holder
                    && b.Effects.TryGetValue(proto.Stat, out var d) && d > 0);
                return (met, met ? "закон принят" : "закона нет");
            }
            case SWAmbitionKind.PassOwnLaws:
            {
                var holder = OwnerHolder(active.Owner);
                var n = holder == null ? 0 : _senate.Bills.Count(b => b.Stage == SWBillStage.Passed && b.SpecialMotionType == null && b.Author == holder);
                return (n >= proto.Count, $"{n}/{proto.Count}");
            }
            case SWAmbitionKind.PassBills:
            {
                var n = _senate.Bills.Count(b => b.Stage == SWBillStage.Passed && b.SpecialMotionType == null);
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
            case SWAmbitionKind.MinisterMetric:
            {
                var value = MinisterMetricValue(active.Owner, proto.Stat);
                return (Compare(value, proto.Comparison, proto.Threshold), StatProgress(value, proto));
            }
            case SWAmbitionKind.CourtMetric:
            {
                var value = CourtMetricValue(proto.Stat);
                return (Compare(value, proto.Comparison, proto.Threshold), StatProgress(value, proto));
            }
            case SWAmbitionKind.PersonMetric:
            {
                var holder = OwnerHolder(active.Owner);
                var value = holder == null ? 0f : _merc.PersonMetricValue(holder, proto.Stat);
                return (Compare(value, proto.Comparison, proto.Threshold), StatProgress(value, proto));
            }
            case SWAmbitionKind.CommitteeMetric:
            {
                var holder = OwnerHolder(active.Owner);
                var value = holder == null ? 0f : _committee.CommitteeMetricValue(holder, proto.Stat);
                return (Compare(value, proto.Comparison, proto.Threshold), StatProgress(value, proto));
            }
            default:
                return (false, string.Empty);
        }
    }

    // Generic (ministryId, metric) lookup for every Minister-role ambition. One switch per office keeps
    // each ministry/department/bureau's ambitions grounded in that office's own real mechanics instead of
    // generic global stats.
    private float MinisterMetricValue(string ministryId, string metric) => ministryId switch
    {
        SWMinistryDefenseSystem.DefenseMinistryId => metric switch
        {
            "BorderPressure" => _defense.BorderPressure,
            "WarWeariness" => _defense.WarWeariness,
            "FleetSize" => _defense.FleetSize,
            "FleetActive" => _defense.FleetActive,
            _ => 0f,
        },
        SWMinistryInternalSecuritySystem.SecurityMinistryId => metric switch
        {
            "Threat" => _security.Threat,
            "Unrest" => _security.Unrest,
            "PlotsBusted" => _security.PlotsBusted,
            _ => 0f,
        },
        SWMinistryEconomySystem.EconomyMinistryId => metric switch
        {
            "Growth" => _economy.Growth,
            "Overheat" => _economy.Overheat,
            "ProjectsCompleted" => _economy.ProjectsCompleted,
            "ActiveRoutes" => _economy.ActiveRoutes,
            _ => 0f,
        },
        SWMinistryFinanceSystem.FinanceMinistryId => metric switch
        {
            "Inflation" => _finance.Inflation,
            "Debt" => _finance.Debt,
            "RatingBand" => _finance.RatingBand,
            _ => 0f,
        },
        SWMinistryJusticeSystem.JusticeMinistryId => metric switch
        {
            "Backlog" => _justice.Backlog,
            "Accuracy" => _justice.Accuracy(),
            "Resolved" => _justice.Resolved,
            "Overdue" => _justice.Overdue,
            _ => 0f,
        },
        SWMinistryHealthSystem.HealthMinistryId => metric switch
        {
            "Immunity" => _health.Immunity,
            "HospitalsBuilt" => _health.HospitalsBuilt,
            "VaccineReady" => _health.VaccineReady ? 1f : 0f,
            _ => 0f,
        },
        SWDeptHealthcareSystem.DeptId => metric switch
        {
            "Readiness" => _deptHealth.Readiness,
            "Supplies" => _deptHealth.Supplies,
            "SuccessfulMissions" => _deptHealth.SuccessfulMissions,
            _ => 0f,
        },
        SWDeptGuardSystem.DeptId => metric switch
        {
            "Discipline" => _deptGuard.Discipline,
            "TotalSquads" => _deptGuard.TotalSquads,
            "WritsExecuted" => _deptGuard.WritsExecuted,
            "PostedTotal" => _deptGuard.PostedTotal,
            _ => 0f,
        },
        SWDeptArbitrageSystem.DeptId => metric switch
        {
            "Independence" => _deptArbitrage.Independence,
            "Solved" => _deptArbitrage.Solved,
            _ => 0f,
        },
        SWDeptExpenditureSystem.DeptId => metric switch
        {
            "Transparency" => _deptExpenditure.Transparency,
            "Recovered" => _deptExpenditure.Recovered,
            _ => 0f,
        },
        SWBureauShipsSystem.BureauId => metric switch
        {
            "TradeFlow" => _bureauShips.TradeFlow,
            "Integrity" => _bureauShips.Integrity,
            "Network" => _bureauShips.Network,
            "ForgedTotal" => _bureauShips.ForgedTotal,
            "FleetRegistered" => _bureauShips.FleetRegistered,
            _ => 0f,
        },
        SWBureauWeaponsSystem.BureauId => metric switch
        {
            "Control" => _bureauWeapons.Control,
            "BlackMarket" => _bureauWeapons.BlackMarket(),
            "SecurityDroids" => _bureauWeapons.SecurityDroids,
            _ => 0f,
        },
        SWBureauTransitSystem.BureauId => metric switch
        {
            "Mobility" => _bureauTransit.Mobility,
            "Safety" => _bureauTransit.Safety,
            "Congestion" => _bureauTransit.Congestion,
            _ => 0f,
        },
        _ => 0f,
    };

    private float CourtMetricValue(string metric) => metric switch
    {
        "CasesResolved" => _court.CasesResolved,
        "CasesUnresolved" => _court.CasesUnresolved,
        "LawsRepealed" => _court.LawsRepealed,
        "VerdictsForPlaintiff" => _court.VerdictsForPlaintiff,
        "VerdictsForDefendant" => _court.VerdictsForDefendant,
        _ => 0f,
    };

    private static string StatProgress(float value, SWAmbitionPrototype proto)
    {
        var sign = proto.Comparison == SWAmbitionComparison.Above ? ">" : "<";
        return $"{value:0} / нужно {sign}{proto.Threshold:0}";
    }

    private void OnRoundEnd(RoundEndTextAppendEvent args)
    {
        // Make sure everyone holding a seat at round end is scored, even if they never opened the console.
        foreach (var (sectorId, _) in _senate.SectorHolders)
            EnsureAssigned(sectorId, SWAmbitionRole.Senator, sectorId, null);
        if (_senate.Chancellor != null)
            EnsureAssigned(SWSenateDilemmaSystem.RepublicOwner, SWAmbitionRole.Chancellor, null, null);
        foreach (var (ministryId, _) in _ministry.Holders)
            EnsureAssigned(ministryId, SWAmbitionRole.Minister, null, ministryId);
        EnsureCourtAssigned();

        if (_assigned.Count == 0)
            return;

        var standings = new List<(string Label, int Score, int Total, List<string> Lines)>();
        foreach (var (owner, ambitions) in _assigned)
        {
            if (ambitions.Count == 0)
                continue;

            var holder = OwnerHolder(owner) ?? "-";
            var label = owner == SWSenateDilemmaSystem.RepublicOwner
                ? $"Канцлер ({holder})"
                : owner.StartsWith("court-")
                    ? $"{CourtSeatLabel(owner)} - {holder}"
                    : _proto.TryIndex<SWMinistryPrototype>(owner, out var ministryProto)
                        ? $"{ministryProto.Name} - {holder}"
                        : $"{SectorName(owner)} - {holder}";

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
            args.AddLine($"[bold]{standing.Label}[/bold] - {standing.Score}/{standing.Total} очк.");
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

    private string? OwnerHolder(string owner)
    {
        if (owner == SWSenateDilemmaSystem.RepublicOwner)
            return _senate.Chancellor;
        if (owner == "court-chief")
            return _court.Chief;
        if (owner.StartsWith("court-judge-"))
        {
            return int.TryParse(owner["court-judge-".Length..], out var index) && index < _court.Judges.Count
                ? _court.Judges[index]
                : null;
        }
        if (owner == "court-prosecutor")
            return _court.Prosecutor;
        if (owner == "court-defender")
            return _court.Defender;
        if (_senate.SectorGlobals.ContainsKey(owner))
            return _senate.GetSectorHolder(owner);
        return _ministry.GetHolder(owner);
    }

    private static string CourtSeatLabel(string owner) => owner switch
    {
        "court-chief" => "Верховный Судья",
        "court-prosecutor" => "Обвинитель",
        "court-defender" => "Защитник",
        _ => "Республиканский Судья",
    };

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
