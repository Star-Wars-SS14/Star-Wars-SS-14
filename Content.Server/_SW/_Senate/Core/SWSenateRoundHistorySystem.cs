using System.Linq;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Sectors;
using Content.Shared.GameTicking;
using Content.Server._SW._Senate.Economy;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._SW._Senate.Core;

/// <summary>Captures only public, confirmed facts for the completed Senate session.</summary>
public sealed class SWSenateRoundHistorySystem : EntitySystem
{
    [Dependency] private readonly SWSenateStateSystem _state = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly SWMinistryEconomySystem _economy = default!;
    [Dependency] private readonly SWMinistryFinanceSystem _finance = default!;
    [Dependency] private readonly SWMinistryHealthSystem _health = default!;
    [Dependency] private readonly SWSenateBudgetSystem _budget = default!;
    [Dependency] private readonly SWSenateCourtSystem _court = default!;
    [Dependency] private readonly IPlayerManager _players = default!;

    private Dictionary<string, float>? _start;
    private int _startDay;
    private Dictionary<string, Dictionary<string, float>>? _startSectors;
    private SWSenateRoundHistory? _snapshot;
    private RoundEndMessageEvent? _roundEndMessage;
    public bool Ending { get; private set; }
    public bool SessionActive => _start != null;

    public override void Initialize()
    {
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => Reset());
        _players.PlayerStatusChanged += OnStatusChanged;
    }

    public override void Shutdown()
    {
        _players.PlayerStatusChanged -= OnStatusChanged;
        base.Shutdown();
    }

    private void OnStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus == SessionStatus.InGame && _roundEndMessage != null)
            RaiseNetworkEvent(_roundEndMessage, args.Session);
    }

    public void RememberScoreboard(RoundEndMessageEvent message)
    {
        if (!Ending || message.SenateHistory == null || _roundEndMessage != null)
            return;
        _roundEndMessage = new RoundEndMessageEvent(message.GamemodeTitle, message.RoundEndText,
            message.RoundDuration, message.RoundId, message.PlayerCount, message.AllPlayersEndInfo, null)
            { SenateHistory = message.SenateHistory };
    }

    private void Reset()
    {
        _start = null;
        _startSectors = null;
        _snapshot = null;
        _roundEndMessage = null;
        Ending = false;
    }

    public void StartSession()
    {
        // Rule restarts within one round do not reset laws or economic history.
        if (_start != null)
            return;
        _start = _state.GetRepublicGlobals();
        _startSectors = _state.SectorGlobals.ToDictionary(p => p.Key, p => new Dictionary<string, float>(p.Value));
        _startDay = _time.Day;
    }

    public void MarkEnding() => Ending = _start != null;

    public SWSenateRoundHistory? Capture()
    {
        if (!Ending || _start == null)
            return null;
        if (_snapshot != null)
            return _snapshot;

        var final = _state.GetRepublicGlobals();
        var stats = _prototypes.EnumeratePrototypes<SWGlobalStatPrototype>()
            .OrderBy(p => p.Order).ThenBy(p => p.ID)
            .Where(p => _start.ContainsKey(p.ID) && final.ContainsKey(p.ID))
            .Take(32)
            .Select(p => new SWSenateHistoryStat { Name = Limit(p.Name), Start = _start[p.ID], End = final[p.ID] })
            .ToArray();
        var laws = _state.Bills.Where(b => b.Stage == SWBillStage.Passed && !b.IsElection && b.SpecialMotionType == null && !b.IsDecree)
            .Where(b => !string.IsNullOrWhiteSpace(b.Name))
            .Select(b => new SWSenateHistoryLaw { Name = Limit(b.Name), Author = string.IsNullOrWhiteSpace(b.Author) ? "неизвестен" : Limit(b.Author), Amendment = b.AmendsLawId != null, Repealed = b.Repealed })
            .ToArray();
        var sectors = _prototypes.EnumeratePrototypes<SWSectorPrototype>()
            .OrderBy(p => p.Name, StringComparer.Ordinal).Take(100)
            .Where(p => _startSectors != null && _startSectors.ContainsKey(p.ID) && _state.SectorGlobals.ContainsKey(p.ID))
            .Select(p => new SWSenateHistorySector
            {
                Name = Limit(p.Name),
                Stats = _prototypes.EnumeratePrototypes<SWGlobalStatPrototype>().OrderBy(g => g.Order).Take(32)
                    .Where(g => _startSectors![p.ID].ContainsKey(g.ID) && _state.SectorGlobals[p.ID].ContainsKey(g.ID))
                    .Select(g => new SWSenateHistoryStat { Name = Limit(g.Name), Start = _startSectors![p.ID][g.ID], End = _state.SectorGlobals[p.ID][g.ID] }).ToArray(),
            }).ToArray();
        var projects = _economy.BuildIndustrialProjects().Where(p => p.Completed).Take(50)
            .Select(p => Limit($"{p.Name} — {p.WorldName}")).ToArray();
        var events = _economy.BuildEconomyEvents().Take(20)
            .Select(e => Limit($"{e.Title} — {e.WorldName}: {e.Status}")).ToArray();
        _snapshot = new SWSenateRoundHistory
        {
            Days = Math.Max(1, _time.Day - _startDay + 1),
            Stats = stats,
            Sectors = sectors,
            Projects = projects,
            Events = events,
            DepartmentSummary = new[]
            {
                $"Финансы: инфляция {_finance.Inflation}%, ключевая ставка {_finance.KeyRate}%, долг {_finance.CreditDebt} кр.; нераспределённая казна {_budget.Treasury} кр.",
                $"Здравоохранение: больниц введено в работу — {_health.HospitalsBuilt}, обеспеченность медицинской помощью — {_health.HospitalServicePercent}%.",
                $"Суд: завершено дел с решением — {_court.CasesResolved}, закрыто без решения — {_court.CasesUnresolved}; отменено законов — {_court.LawsRepealed}.",
            },
            Laws = laws.Take(50).ToArray(),
            AdditionalLaws = Math.Max(0, laws.Length - 50),
        };
        return _snapshot;
    }

    private static string Limit(string value) => value.Length <= 160 ? value : value[..160] + "…";
}
