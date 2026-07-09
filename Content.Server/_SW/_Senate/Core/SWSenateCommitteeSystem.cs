using System;
using System.Linq;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Sectors;
using Content.Shared.GameTicking;
using Robust.Shared.Prototypes;

namespace Content.Server._SW._Senate.Core;

// Standing legislative committees. Any sitting senator may join any committee with a free seat.
// The first to join a committee chairs it; if the chair leaves, members vote in-console for a new
// chair and the ballot is settled at the next committee hour (or immediately once one candidate
// holds a majority). Bill referral and markup are layered on in later stages.
public sealed class SWSenateCommitteeSystem : EntitySystem
{
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly SWSenateStateSystem _senate = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    public const int SeatsPerCommittee = 3;
    public const float CommitteeWeightThreshold = 8f;
    public static int CommitteeHour => SWSenateTimeSystem.StartHour + 1;

    public bool InCommitteeHour => _time.DayActive && _time.CurrentHour == CommitteeHour;

    private bool PastCommitteeHour => _time.DayActive && _time.CurrentHour > CommitteeHour;

    // Which committee owns each policy domain (global stat). Bills route to the committee holding the
    // largest share of their effect weight.
    private static readonly Dictionary<string, string> GlobalToCommittee = new()
    {
        ["SWGlobalDefense"] = "defense",
        ["SWGlobalSecurity"] = "defense",
        ["SWGlobalForeign"] = "defense",
        ["SWGlobalEconomy"] = "finance",
        ["SWGlobalIndustry"] = "finance",
        ["SWGlobalHealthcare"] = "welfare",
        ["SWGlobalEducation"] = "welfare",
        ["SWGlobalSociety"] = "justice",
    };

    private readonly Dictionary<string, string> _statToCommittee = new();

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
    private readonly Dictionary<int, Dictionary<string, bool>> _billVotes = new();
    private readonly Dictionary<int, string> _billReason = new();
    private readonly Dictionary<string, int> _advanced = new();
    private readonly Dictionary<string, int> _returned = new();
    private int _lastResolveDay = -1;
    private int _lastDischargeDay = -1;

    public override void Initialize()
    {
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        BuildCommittees();
        BuildStatMap();
    }

    private void BuildStatMap()
    {
        _statToCommittee.Clear();
        foreach (var (globalId, committeeId) in GlobalToCommittee)
            _statToCommittee[globalId] = committeeId;
        foreach (var stat in _proto.EnumeratePrototypes<SWStatPrototype>())
        {
            if (GlobalToCommittee.TryGetValue(stat.Global.Id, out var committeeId))
                _statToCommittee[stat.ID] = committeeId;
        }
    }

    // Returns the committee a bill should be referred to, or null to send it straight to the floor.
    public string? RouteBill(IReadOnlyDictionary<string, float> effects)
    {
        if (effects.Count == 0)
            return null;

        var weights = new Dictionary<string, float>();
        var total = 0f;
        foreach (var (statId, delta) in effects)
        {
            var mag = MathF.Abs(delta);
            total += mag;
            if (_statToCommittee.TryGetValue(statId, out var committeeId))
                weights[committeeId] = weights.GetValueOrDefault(committeeId) + mag;
        }

        if (total < CommitteeWeightThreshold || weights.Count == 0)
            return null;

        string? best = null;
        var bestWeight = -1f;
        foreach (var committee in _committees)
        {
            if (weights.TryGetValue(committee.Id, out var weight) && weight > bestWeight)
            {
                best = committee.Id;
                bestWeight = weight;
            }
        }

        return best;
    }

    public string CommitteeName(string committeeId) =>
        _committees.FirstOrDefault(c => c.Id == committeeId)?.Name ?? committeeId;

    // Committee members vote per bill during the committee hour: advance to the floor, or return it to
    // the author with a reason (capped at two returns). Resolves as soon as a majority agrees.
    public bool VoteBill(string voter, string committeeId, int billId, bool advance, string reason)
    {
        if (!InCommitteeHour)
            return false;

        var committee = _committees.FirstOrDefault(c => c.Id == committeeId);
        if (committee == null || !committee.Members.Contains(voter))
            return false;
        if (!_senate.GetInCommitteeBills(committeeId).Any(b => b.Id == billId))
            return false;

        if (!_billVotes.TryGetValue(billId, out var votes))
        {
            votes = new Dictionary<string, bool>();
            _billVotes[billId] = votes;
        }

        votes[voter] = advance;
        if (!advance)
            _billReason[billId] = reason;

        ResolveBill(committee, billId);
        return true;
    }

    private void ResolveBill(Committee committee, int billId)
    {
        if (!_billVotes.TryGetValue(billId, out var votes))
            return;

        var advanceCount = 0;
        var sendBackCount = 0;
        foreach (var (voter, advance) in votes)
        {
            if (!committee.Members.Contains(voter))
                continue;
            if (advance)
                advanceCount++;
            else
                sendBackCount++;
        }

        var need = committee.Members.Count / 2 + 1;

        if (sendBackCount >= need && _senate.SendBackBill(billId, _billReason.GetValueOrDefault(billId, string.Empty)))
        {
            Credit(committee, _returned);
            ClearBillVotes(billId);
            return;
        }

        if (advanceCount >= need)
        {
            _senate.AdvanceToFloor(billId);
            Credit(committee, _advanced);
            ClearBillVotes(billId);
        }
    }

    private static void Credit(Committee committee, Dictionary<string, int> tally)
    {
        foreach (var member in committee.Members)
            tally[member] = tally.GetValueOrDefault(member) + 1;
    }

    public float CommitteeMetricValue(string person, string metric) => metric switch
    {
        "IsChair" => _committees.Any(c => c.Chair == person) ? 1f : 0f,
        "BillsAdvanced" => _advanced.GetValueOrDefault(person),
        "BillsReturned" => _returned.GetValueOrDefault(person),
        _ => 0f,
    };

    private void ClearBillVotes(int billId)
    {
        _billVotes.Remove(billId);
        _billReason.Remove(billId);
    }

    public List<SWCommitteeBillEntry> BuildCommitteeBills(string committeeId, string? viewer)
    {
        var list = new List<SWCommitteeBillEntry>();
        var committee = _committees.FirstOrDefault(c => c.Id == committeeId);
        if (committee == null)
            return list;

        var canAct = viewer != null && committee.Members.Contains(viewer) && InCommitteeHour;
        var need = committee.Members.Count / 2 + 1;

        foreach (var bill in _senate.GetInCommitteeBills(committeeId))
        {
            _billVotes.TryGetValue(bill.Id, out var votes);
            var advanceCount = 0;
            var sendBackCount = 0;
            var myVote = 0;
            if (votes != null)
            {
                foreach (var (voter, advance) in votes)
                {
                    if (!committee.Members.Contains(voter))
                        continue;
                    if (advance)
                        advanceCount++;
                    else
                        sendBackCount++;
                }

                if (viewer != null && votes.TryGetValue(viewer, out var mine))
                    myVote = mine ? 1 : 2;
            }

            list.Add(new SWCommitteeBillEntry
            {
                Id = bill.Id,
                Name = bill.Name,
                Author = bill.Author,
                Effects = _senate.BuildBillEffects(bill),
                SendBacks = bill.SendBacks,
                CanSendBack = bill.SendBacks < 2,
                AdvanceVotes = advanceCount,
                SendBackVotes = sendBackCount,
                VotesNeeded = need,
                MyVote = myVote,
                CanAct = canAct,
            });
        }

        return list;
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
        _billVotes.Clear();
        _billReason.Clear();
        _advanced.Clear();
        _returned.Clear();
        _lastResolveDay = -1;
        _lastDischargeDay = -1;
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev) => BuildCommittees();

    public override void Update(float frameTime)
    {
        if (!_time.ModeActive)
            return;

        // Committee convenes: settle chair elections and prune departed members.
        if (InCommitteeHour && _lastResolveDay != _time.Day)
        {
            _lastResolveDay = _time.Day;
            foreach (var committee in _committees)
            {
                Prune(committee);
                if (committee.ElectionOpen)
                    ResolveElection(committee);
            }
        }

        // Committee adjourns: bills the committee did not act on this session are auto-discharged.
        if (PastCommitteeHour && _lastDischargeDay != _time.Day)
        {
            _lastDischargeDay = _time.Day;
            _senate.DischargeReferredBills(_time.Day);
            foreach (var billId in _billVotes.Keys.ToList())
            {
                if (!_senate.IsInCommittee(billId))
                    ClearBillVotes(billId);
            }
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
