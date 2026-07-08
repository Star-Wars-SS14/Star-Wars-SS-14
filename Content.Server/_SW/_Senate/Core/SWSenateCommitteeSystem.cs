using System.Linq;
using Content.Shared._SW._Senate;
using Content.Shared.GameTicking;

namespace Content.Server._SW._Senate.Core;

// Standing legislative committees. Any sitting senator may join any committee with a free seat.
// The first to join a committee chairs it; if the chair leaves, members vote in-console for a new
// chair and the ballot is settled at the next committee hour (or immediately once one candidate
// holds a majority). Bill referral and markup are layered on in later stages.
public sealed class SWSenateCommitteeSystem : EntitySystem
{
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly SWSenateStateSystem _senate = default!;

    public const int SeatsPerCommittee = 3;
    public static int CommitteeHour => SWSenateTimeSystem.StartHour + 1;

    public bool InCommitteeHour => _time.DayActive && _time.CurrentHour == CommitteeHour;

    private sealed class Committee
    {
        public string Id = string.Empty;
        public string Name = string.Empty;
        public string Description = string.Empty;
        public readonly List<string> Members = new();
        public string? Chair;
        public bool ElectionOpen;
        public readonly Dictionary<string, string> Votes = new();
    }

    private readonly List<Committee> _committees = new();
    private int _lastResolveDay = -1;

    public override void Initialize()
    {
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        BuildCommittees();
    }

    private void BuildCommittees()
    {
        _committees.Clear();
        _committees.Add(new Committee
        {
            Id = "defense",
            Name = "Комитет обороны и безопасности",
            Description = "Военные бюджеты, оборонные и охранные полномочия, тревоги, антитеррор.",
        });
        _committees.Add(new Committee
        {
            Id = "finance",
            Name = "Комитет финансов и бюджета",
            Description = "Расходные законы, налоги, казна, экономические программы и торговля.",
        });
        _committees.Add(new Committee
        {
            Id = "welfare",
            Name = "Комитет благополучия",
            Description = "Здравоохранение, социальные расходы, развитие секторов, эпидемии.",
        });
        _committees.Add(new Committee
        {
            Id = "justice",
            Name = "Комитет юстиции и регламента",
            Description = "Правовые законы, регламент Сената, наделение полномочиями, борьба с коррупцией.",
        });
        _lastResolveDay = -1;
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev) => BuildCommittees();

    public override void Update(float frameTime)
    {
        if (!_time.ModeActive)
            return;
        if (!InCommitteeHour || _lastResolveDay == _time.Day)
            return;

        _lastResolveDay = _time.Day;

        foreach (var committee in _committees)
        {
            Prune(committee);
            if (committee.ElectionOpen)
                ResolveElection(committee);
        }
    }

    private bool IsSenator(string person) => _senate.SectorHolders.Values.Contains(person);

    public bool IsAnyMember(string person) => _committees.Any(c => c.Members.Contains(person));

    public bool Join(string person, string committeeId)
    {
        if (!IsSenator(person) || IsAnyMember(person))
            return false;

        var committee = _committees.FirstOrDefault(c => c.Id == committeeId);
        if (committee == null || committee.Members.Count >= SeatsPerCommittee)
            return false;

        committee.Members.Add(person);
        if (committee.Chair == null && !committee.ElectionOpen)
            committee.Chair = person;

        return true;
    }

    public bool Leave(string person, string committeeId)
    {
        var committee = _committees.FirstOrDefault(c => c.Id == committeeId);
        if (committee == null || !committee.Members.Contains(person))
            return false;

        committee.Members.Remove(person);
        committee.Votes.Remove(person);
        foreach (var voter in committee.Votes.Where(kv => kv.Value == person).Select(kv => kv.Key).ToList())
            committee.Votes.Remove(voter);

        if (committee.Chair == person)
            VacateChair(committee);

        return true;
    }

    public bool VoteChair(string voter, string committeeId, string candidate)
    {
        var committee = _committees.FirstOrDefault(c => c.Id == committeeId);
        if (committee == null || !committee.ElectionOpen)
            return false;
        if (!committee.Members.Contains(voter) || !committee.Members.Contains(candidate))
            return false;

        committee.Votes[voter] = candidate;

        var need = committee.Members.Count / 2 + 1;
        var top = committee.Votes.Values.GroupBy(v => v).Select(g => g.Count()).DefaultIfEmpty(0).Max();
        if (top >= need)
            ResolveElection(committee);

        return true;
    }

    private void VacateChair(Committee committee)
    {
        committee.Chair = null;
        committee.Votes.Clear();
        if (committee.Members.Count == 1)
            committee.Chair = committee.Members[0];
        else if (committee.Members.Count > 1)
            committee.ElectionOpen = true;
    }

    private void ResolveElection(Committee committee)
    {
        committee.ElectionOpen = false;
        committee.Chair = committee.Members.Count == 0 ? null : TallyWinner(committee);
        committee.Votes.Clear();
    }

    private static string TallyWinner(Committee committee)
    {
        var counts = new Dictionary<string, int>();
        foreach (var candidate in committee.Votes.Values)
            counts[candidate] = counts.GetValueOrDefault(candidate) + 1;

        string? best = null;
        var bestVotes = -1;
        foreach (var member in committee.Members)
        {
            var votes = counts.GetValueOrDefault(member);
            if (votes > bestVotes)
            {
                best = member;
                bestVotes = votes;
            }
        }

        return best ?? committee.Members[0];
    }

    private void Prune(Committee committee)
    {
        committee.Members.RemoveAll(m => !IsSenator(m));

        foreach (var voter in committee.Votes.Keys.ToList())
        {
            if (!committee.Members.Contains(voter) || !committee.Members.Contains(committee.Votes[voter]))
                committee.Votes.Remove(voter);
        }

        if (committee.Chair != null && !committee.Members.Contains(committee.Chair))
        {
            VacateChair(committee);
        }
        else if (committee.Chair == null && !committee.ElectionOpen)
        {
            if (committee.Members.Count == 1)
                committee.Chair = committee.Members[0];
            else if (committee.Members.Count > 1)
                committee.ElectionOpen = true;
        }
    }

    public List<SWCommitteeEntry> BuildCommittees(string? viewer)
    {
        var canJoin = viewer != null && IsSenator(viewer) && !IsAnyMember(viewer);
        var list = new List<SWCommitteeEntry>();

        foreach (var committee in _committees)
        {
            var iAmMember = viewer != null && committee.Members.Contains(viewer);

            var entry = new SWCommitteeEntry
            {
                Id = committee.Id,
                Name = committee.Name,
                Description = committee.Description,
                ChairName = committee.Chair ?? string.Empty,
                Seats = SeatsPerCommittee,
                MemberCount = committee.Members.Count,
                IAmMember = iAmMember,
                IAmChair = viewer != null && committee.Chair == viewer,
                CanJoin = canJoin && !iAmMember && committee.Members.Count < SeatsPerCommittee,
                CanLeave = iAmMember,
                ElectionOpen = committee.ElectionOpen,
            };

            if (iAmMember && committee.ElectionOpen && committee.Votes.TryGetValue(viewer!, out var myVote))
            {
                entry.IVoted = true;
                entry.MyVote = myVote;
            }

            var counts = new Dictionary<string, int>();
            foreach (var candidate in committee.Votes.Values)
                counts[candidate] = counts.GetValueOrDefault(candidate) + 1;

            foreach (var member in committee.Members)
            {
                entry.Members.Add(new SWCommitteeMemberEntry
                {
                    Name = member,
                    IsChair = committee.Chair == member,
                    IsMe = member == viewer,
                    Votes = counts.GetValueOrDefault(member),
                });
            }

            list.Add(entry);
        }

        return list;
    }
}
