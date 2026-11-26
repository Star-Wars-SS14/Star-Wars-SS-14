using System.Linq;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Sectors;
using Content.Shared.GameTicking;
using Content.Server._SW._Senate.Economy;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Inventory;
using Robust.Shared.Network;

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
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly SharedIdCardSystem _idCard = default!;

    private Dictionary<string, float>? _start;
    private int _startDay;
    private Dictionary<string, Dictionary<string, float>>? _startSectors;
    private SWSenateRoundHistory? _snapshot;
    private RoundEndMessageEvent? _roundEndMessage;
    private readonly HashSet<long> _seenVoteResolutions = new();
    private readonly List<SWSenateCloseVote> _closeVotes = new();
    private readonly HashSet<int> _closeBillIds = new();
    private int _closestVote = int.MaxValue;
    private readonly HashSet<(EntityUid Card, string Name)> _participants = new();
    private readonly List<string> _completedAmbitions = new();
    private readonly Dictionary<int, (NetUserId Player, EntityUid Card, string Name)> _billAuthors = new();
    private readonly Dictionary<(EntityUid Card, string Name), List<string>> _titles = new();
    public bool Ending { get; private set; }
    public bool SessionActive => _start != null;
    public int? ScoreboardRoundId => _roundEndMessage?.RoundId;

    public override void Initialize()
    {
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => Reset());
        SubscribeLocalEvent<SWSenateBillVotesResolvedEvent>(OnVoteResolved);
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawned);
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

    public SWSenatePersonalCharacter[] PersonalCharacters(NetUserId user) =>
        _roundEndMessage?.AllPlayersEndInfo.Where(info => info.PlayerGuid == user)
            .Select(info => new SWSenatePersonalCharacter
            {
                Name = Limit(info.PlayerICName ?? ""),
                Role = info.Role,
                Observer = info.Observer,
            }).ToArray() ?? Array.Empty<SWSenatePersonalCharacter>();

    private void Reset()
    {
        _start = null;
        _startSectors = null;
        _snapshot = null;
        _roundEndMessage = null;
        _seenVoteResolutions.Clear();
        _closeVotes.Clear();
        _closeBillIds.Clear();
        _closestVote = int.MaxValue;
        _participants.Clear();
        _completedAmbitions.Clear();
        _billAuthors.Clear();
        _titles.Clear();
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

    public void RecordCompletedAmbition(string publicName)
    {
        if (!Ending || _snapshot != null || string.IsNullOrWhiteSpace(publicName)) return;
        _completedAmbitions.Add(Limit(publicName));
    }

    public void RecordBillAuthor(int billId, NetUserId player, EntityUid card, string name)
    {
        if (!_time.ModeActive || Ending || _snapshot != null || string.IsNullOrWhiteSpace(name)) return;
        _billAuthors.TryAdd(billId, (player, card, Limit(name)));
    }

    private void OnPlayerSpawned(PlayerSpawnCompleteEvent ev)
    {
        if (!_time.ModeActive || !_inventory.TryGetSlotEntity(ev.Mob, "id", out var slot) ||
            !_idCard.TryGetIdCard(slot.Value, out var card))
            return;
        RecordCard(card.Owner);
    }

    /// <summary>Called only for a card in a successful, authoritative office operation.</summary>
    public void RecordCard(EntityUid card)
    {
        if (!_time.ModeActive || Ending || _snapshot != null || !TryComp<IdCardComponent>(card, out var id) ||
            string.IsNullOrWhiteSpace(id.FullName))
            return;
        _participants.Add((card, id.FullName));
        var key = (card, id.FullName);
        if (!_titles.TryGetValue(key, out var titles))
            _titles[key] = titles = new List<string>();
        var title = id.LocalizedJobTitle;
        if (!string.IsNullOrWhiteSpace(title)) title = Limit(title);
        if (!string.IsNullOrWhiteSpace(title) && !titles.Contains(title))
            titles.Add(title);
    }

    private void OnVoteResolved(SWSenateBillVotesResolvedEvent ev)
    {
        if (!SessionActive || Ending || ev.PublicVotesFor is not > 0 || ev.PublicVotesAgainst is not > 0 ||
            !_seenVoteResolutions.Add(ev.ResolutionId))
            return;
        var bill = _state.Bills.FirstOrDefault(b => b.Id == ev.BillId);
        if (bill == null || bill.IsElection || bill.IsDecree || bill.SpecialMotionType != null ||
            bill.AmendsLawId != null || string.IsNullOrWhiteSpace(ev.BillName))
            return;

        var votesFor = ev.PublicVotesFor.Value;
        var votesAgainst = ev.PublicVotesAgainst.Value;
        var difference = Math.Abs(votesFor - votesAgainst);
        if (difference > 1 || difference > _closestVote)
            return;
        if (difference < _closestVote)
        {
            _closeVotes.Clear();
            _closeBillIds.Clear();
            _closestVote = difference;
        }
        _closeVotes.Add(new SWSenateCloseVote { Name = Limit(ev.BillName), For = votesFor, Against = votesAgainst });
        _closeBillIds.Add(ev.BillId);
    }

    public SWSenateRoundHistory? Capture()
    {
        if (!Ending || _start == null)
            return null;
        if (_snapshot != null)
            return _snapshot;

        var final = _state.GetRepublicGlobals();
        var passedBills = _state.Bills.Where(b => b.Stage == SWBillStage.Passed && !b.IsElection && b.SpecialMotionType == null && !b.IsDecree && !string.IsNullOrWhiteSpace(b.Name)).ToArray();
        var stats = _prototypes.EnumeratePrototypes<SWGlobalStatPrototype>()
            .OrderBy(p => p.Order).ThenBy(p => p.ID)
            .Where(p => _start.ContainsKey(p.ID) && final.ContainsKey(p.ID))
            .Take(32)
            .Select(p => new SWSenateHistoryStat { Id = p.ID, Name = Limit(p.Name), Min = p.Min, Max = p.Max, Start = _start[p.ID], End = final[p.ID] })
            .ToArray();
        var laws = _state.Bills.Where(b => b.Stage == SWBillStage.Passed && !b.IsElection && b.SpecialMotionType == null && !b.IsDecree)
            .Where(b => !string.IsNullOrWhiteSpace(b.Name))
            .Select(b => new SWSenateHistoryLaw { Name = Limit(b.Name), Author = string.IsNullOrWhiteSpace(b.Author) ? "неизвестен" : b.Author, Amendment = b.AmendsLawId != null, Repealed = b.Repealed })
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
        var awards = BuildAwards(passedBills, projects);
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
            Laws = laws.Take(50).Select(law => new SWSenateHistoryLaw { Name = law.Name, Author = Limit(law.Author), Amendment = law.Amendment, Repealed = law.Repealed }).ToArray(),
            AdditionalLaws = Math.Max(0, laws.Length - 50),
            Participants = _participants.Select(entry => Limit(entry.Name)).OrderBy(name => name, StringComparer.Ordinal).ToArray(),
            CompletedAmbitions = _completedAmbitions.OrderBy(name => name, StringComparer.Ordinal).ToArray(),
            FinaleParticipants = _titles.OrderBy(entry => entry.Key.Name, StringComparer.Ordinal).Select(entry => new SWSenateFinaleParticipant { Name = Limit(entry.Key.Name), Titles = entry.Value.ToArray() }).ToArray(),
            PassedLaws = laws.Count(law => !law.Amendment),
            PassedAmendments = laws.Count(law => law.Amendment),
            CloseVotes = _closeVotes.ToArray(),
            Awards = awards.Take(16).ToArray(),
            AdditionalAwards = Math.Max(0, awards.Length - 16),
        };
        return _snapshot;
    }

    private static string Limit(string value) => value.Length <= 160 ? value : value[..160] + "…";

    private SWSenateFinaleAward[] BuildAwards(SWSenateBill[] passed, string[] projects)
    {
        var awards = new List<SWSenateFinaleAward>();
        var recognizedBills = new HashSet<int>();
        var awardedConditions = new HashSet<SWSenateAwardCondition>();
        foreach (var proto in _prototypes.EnumeratePrototypes<SWSenateFinaleAwardPrototype>().OrderByDescending(p => p.Priority).ThenBy(p => p.ID, StringComparer.Ordinal))
        {
            if (proto.MinThreshold < 1 || awardedConditions.Contains(proto.Condition)) continue;
            var recipients = new List<string>();
            switch (proto.Condition)
            {
                case SWSenateAwardCondition.CloseVote when proto.Recipient == SWSenateAwardRecipient.Project:
                    if (_closeVotes.Count >= proto.MinThreshold)
                    {
                        recipients.AddRange(_closeVotes.Select(v => $"{v.Name} — за {v.For}, против {v.Against}"));
                        recognizedBills.UnionWith(_closeBillIds);
                    }
                    break;
                case SWSenateAwardCondition.AuthoredLaw or SWSenateAwardCondition.AuthoredAmendment when proto.Recipient == SWSenateAwardRecipient.Author:
                    var amendment = proto.Condition == SWSenateAwardCondition.AuthoredAmendment;
                    var groups = passed.Where(b => (b.AmendsLawId != null) == amendment && _billAuthors.ContainsKey(b.Id))
                        .GroupBy(b => (_billAuthors[b.Id].Player, _billAuthors[b.Id].Card, _billAuthors[b.Id].Name)).ToArray();
                    if (groups.Length == 0) break;
                    var maximum = groups.Max(g => g.Count());
                    if (maximum < proto.MinThreshold) break;
                    foreach (var group in groups.Where(g => g.Count() == maximum).OrderBy(g => g.Key.Name, StringComparer.Ordinal).ThenBy(g => g.Key.Player.ToString(), StringComparer.Ordinal).ThenBy(g => g.Key.Card.ToString(), StringComparer.Ordinal))
                    {
                        recipients.Add($"{_billAuthors[group.First().Id].Name} — принято: {maximum}");
                        foreach (var bill in group) recognizedBills.Add(bill.Id);
                    }
                    break;
                case SWSenateAwardCondition.CompletedProject when proto.Recipient == SWSenateAwardRecipient.Project:
                    if (projects.Length >= proto.MinThreshold) recipients.Add(projects[0]);
                    break;
                case SWSenateAwardCondition.PassedLaw when proto.Recipient == SWSenateAwardRecipient.Law:
                    var available = passed.Where(b => b.AmendsLawId == null && !recognizedBills.Contains(b.Id)).ToArray();
                    if (available.Length >= proto.MinThreshold) recipients.Add(Limit(available[0].Name));
                    break;
            }
            if (recipients.Count == 0) continue;
            awardedConditions.Add(proto.Condition);
            var omitted = Math.Max(0, recipients.Count - 256);
            var visible = recipients.Take(256).ToList();
            if (omitted > 0) visible.Add($"Ещё получателей: {omitted}.");
            var tie = proto.Condition == SWSenateAwardCondition.CloseVote && _closestVote == 0;
            var titleKey = tie && proto.TieTitleKey != null ? proto.TieTitleKey : proto.TitleKey;
            var factKey = tie && proto.TieFactKey != null ? proto.TieFactKey : proto.FactKey;
            awards.Add(new SWSenateFinaleAward { Title = Loc.GetString(titleKey), Fact = Loc.GetString(factKey), Recipients = visible.ToArray() });
        }
        return awards.ToArray();
    }

}
