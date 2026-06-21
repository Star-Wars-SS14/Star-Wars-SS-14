using System.Linq;
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
