using System.Linq;
using Content.Server._SW._Senate.Core;

namespace Content.Server._SW._Senate.Economy;

public sealed class SWSenateBudgetSystem : EntitySystem
{
    [Dependency] private readonly SWSenateStateSystem _senate = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;

    public const int PerSectorBase = 5000;
    private const float ChancellorWeight = 1.5f;

    private int _day = -1;
    private bool _locked;
    private int _frozenTotal;
    private readonly Dictionary<string, int> _alloc = new();
    private readonly Dictionary<string, int> _remaining = new();

    public bool Locked => _locked;
    public IReadOnlyDictionary<string, int> Allocations => _alloc;

    public override void Update(float frameTime)
    {
        if (_time.Day == _day)
            return;

        _day = _time.Day;
        _locked = false;
        _frozenTotal = 0;
        _alloc.Clear();
        _remaining.Clear();
    }

    private float PolicyMultiplier()
    {
        var treasury = _senate.SubStats.TryGetValue("SWStatTreasury", out var t) ? t : 50f;
        var taxes = _senate.SubStats.TryGetValue("SWStatTaxes", out var x) ? x : 50f;
        return Math.Clamp((treasury + taxes) / 100f, 0.4f, 1.8f);
    }

    public int Total()
    {
        if (_locked)
            return _frozenTotal;

        var sectors = _senate.SectorHolders.Count;
        return (int)MathF.Round((sectors + ChancellorWeight) * PerSectorBase * PolicyMultiplier());
    }

    public int Allocated() => _alloc.Values.Sum();

    public int Reserve() => Math.Max(0, Total() - Allocated());

    public bool SetAllocation(string sectorId, int amount)
    {
        if (_locked)
            return false;

        amount = Math.Max(0, amount);
        var others = _alloc.Where(kv => kv.Key != sectorId).Sum(kv => kv.Value);
        var cap = Math.Max(0, Total() - others);
        _alloc[sectorId] = Math.Min(amount, cap);
        return true;
    }

    public bool Lock()
    {
        if (_locked)
            return false;

        _frozenTotal = Total();
        _locked = true;
        _remaining.Clear();
        foreach (var (sector, amount) in _alloc)
            _remaining[sector] = amount;
        _remaining[SWSenateDilemmaSystem.RepublicOwner] = Reserve();
        return true;
    }

    public int Allocation(string sectorId) => _alloc.TryGetValue(sectorId, out var a) ? a : 0;

    public int Available(string owner)
    {
        if (!_locked)
            return 0;

        return _remaining.TryGetValue(owner, out var r) ? r : 0;
    }

    public bool Spend(string owner, int cost)
    {
        if (!_locked || cost < 0)
            return false;

        if (!_remaining.TryGetValue(owner, out var remaining) || remaining < cost)
            return false;

        _remaining[owner] = remaining - cost;
        return true;
    }
}
