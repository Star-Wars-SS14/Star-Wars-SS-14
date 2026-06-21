using System.Linq;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Sectors;
using Robust.Shared.Prototypes;

namespace Content.Server._SW._Senate;

public sealed class SWSenateStateSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;

    private readonly Dictionary<string, Dictionary<string, float>> _sectorStats = new();
    private readonly Dictionary<string, string> _sectorHolders = new();
    private readonly List<SWSenateBill> _bills = new();
    private int _nextBillId = 1;

    private const float DefaultStat = 50f;
    private const float DriftRate = 0.01f;
    private const float UpdateInterval = 5f;

    private float _accumulator;

    public string? Chancellor { get; private set; }
    public string? Speaker { get; private set; }

    public IReadOnlyDictionary<string, Dictionary<string, float>> SectorStats => _sectorStats;
    public IReadOnlyDictionary<string, string> SectorHolders => _sectorHolders;
    public IReadOnlyList<SWSenateBill> Bills => _bills;

    public override void Initialize()
    {
        InitState();
    }

    private void InitState()
    {
        _sectorStats.Clear();
        _sectorHolders.Clear();
        _bills.Clear();
        _nextBillId = 1;
        Chancellor = null;
        Speaker = null;

        foreach (var sector in _proto.EnumeratePrototypes<SWSectorPrototype>())
        {
            var stats = new Dictionary<string, float>();
            foreach (var stat in _proto.EnumeratePrototypes<SWStatPrototype>())
            {
                stats[stat.ID] = sector.StartingStats.TryGetValue(stat.ID, out var value)
                    ? Math.Clamp(value, stat.Min, stat.Max)
                    : DefaultStat;
            }

            _sectorStats[sector.ID] = stats;
        }
    }

    public Dictionary<string, float> GetRepublicStats()
    {
        var result = new Dictionary<string, float>();
        if (_sectorStats.Count == 0)
            return result;

        foreach (var stat in _proto.EnumeratePrototypes<SWStatPrototype>())
        {
            var sum = 0f;
            var count = 0;
            foreach (var (_, stats) in _sectorStats)
            {
                if (!stats.TryGetValue(stat.ID, out var value))
                    continue;
                sum += value;
                count++;
            }

            result[stat.ID] = count > 0 ? sum / count : 0f;
        }

        return result;
    }

    public bool IsClaimed(string sectorId) => _sectorHolders.ContainsKey(sectorId);

    public string? GetSectorHolder(string sectorId) =>
        _sectorHolders.TryGetValue(sectorId, out var holder) ? holder : null;

    public bool TryClaim(string sectorId, string holder)
    {
        if (!_sectorStats.ContainsKey(sectorId) || _sectorHolders.ContainsKey(sectorId))
            return false;

        _sectorHolders[sectorId] = holder;
        return true;
    }

    public void Unclaim(string sectorId) => _sectorHolders.Remove(sectorId);

    public bool TryTakeChancellor(string holder)
    {
        if (Chancellor != null)
            return false;

        Chancellor = holder;
        return true;
    }

    public void ResignChancellor() => Chancellor = null;

    public bool TryTakeSpeaker(string holder)
    {
        if (Speaker != null)
            return false;

        Speaker = holder;
        return true;
    }

    public void ResignSpeaker() => Speaker = null;

    public SWSenateBill SubmitBill(string name, string description, string author, Dictionary<string, float> effects)
    {
        var bill = new SWSenateBill
        {
            Id = _nextBillId++,
            Name = name,
            Description = description,
            Author = author,
            Effects = effects,
            Stage = SWBillStage.Voting,
        };

        _bills.Add(bill);
        return bill;
    }

    public SWSenateBill? GetCurrentBill() => _bills.FirstOrDefault(b => b.Stage == SWBillStage.Voting);

    public bool CastVote(string sectorId, SWVoteChoice choice)
    {
        var bill = GetCurrentBill();
        if (bill == null || !_sectorHolders.ContainsKey(sectorId))
            return false;

        bill.Votes[sectorId] = choice;
        TryResolve(bill);
        return true;
    }

    private void TryResolve(SWSenateBill bill)
    {
        if (_sectorHolders.Count == 0)
            return;

        foreach (var sectorId in _sectorHolders.Keys)
        {
            if (!bill.Votes.ContainsKey(sectorId))
                return;
        }

        var votesFor = bill.Votes.Values.Count(v => v == SWVoteChoice.For);
        var votesAgainst = bill.Votes.Values.Count(v => v == SWVoteChoice.Against);

        if (votesFor > votesAgainst)
        {
            bill.Stage = SWBillStage.Passed;
            ApplyBill(bill);
        }
        else
        {
            bill.Stage = SWBillStage.Rejected;
        }
    }

    public List<SWBillEntry> BuildBillEntries()
    {
        var bills = new List<SWBillEntry>();
        foreach (var bill in _bills)
        {
            var effects = new List<SWBillEffectEntry>();
            foreach (var proto in _proto.EnumeratePrototypes<SWStatPrototype>().OrderBy(s => s.Order))
            {
                if (!bill.Effects.TryGetValue(proto.ID, out var delta))
                    continue;

                effects.Add(new SWBillEffectEntry { StatName = proto.ShortName, Color = proto.Color, Delta = delta });
            }

            bills.Add(new SWBillEntry
            {
                Id = bill.Id,
                Name = bill.Name,
                Author = bill.Author,
                Description = bill.Description,
                Stage = Loc.GetString(StageLoc(bill.Stage)),
                Effects = effects,
            });
        }

        return bills;
    }

    public List<SWBillEffectEntry> BuildBillEffects(SWSenateBill bill)
    {
        var effects = new List<SWBillEffectEntry>();
        foreach (var proto in _proto.EnumeratePrototypes<SWStatPrototype>().OrderBy(s => s.Order))
        {
            if (!bill.Effects.TryGetValue(proto.ID, out var delta))
                continue;

            effects.Add(new SWBillEffectEntry { StatName = proto.ShortName, Color = proto.Color, Delta = delta });
        }

        return effects;
    }

    public static string StageLoc(SWBillStage stage) => stage switch
    {
        SWBillStage.Voting => "sw-senate-bill-stage-voting",
        SWBillStage.Passed => "sw-senate-bill-stage-passed",
        SWBillStage.Rejected => "sw-senate-bill-stage-rejected",
        _ => "sw-senate-bill-stage-voting",
    };

    private void ApplyBill(SWSenateBill bill)
    {
        foreach (var (statId, delta) in bill.Effects)
        {
            if (!_proto.TryIndex<SWStatPrototype>(statId, out var statProto))
                continue;

            foreach (var (_, stats) in _sectorStats)
            {
                if (!stats.TryGetValue(statId, out var value))
                    continue;

                stats[statId] = Math.Clamp(value + delta, statProto.Min, statProto.Max);
            }
        }
    }

    public override void Update(float frameTime)
    {
        _accumulator += frameTime;
        if (_accumulator < UpdateInterval)
            return;

        _accumulator = 0f;

        var republic = GetRepublicStats();
        foreach (var (_, stats) in _sectorStats)
        {
            foreach (var statId in stats.Keys.ToList())
            {
                if (!republic.TryGetValue(statId, out var target))
                    continue;

                stats[statId] += (target - stats[statId]) * DriftRate;
            }
        }
    }
}
