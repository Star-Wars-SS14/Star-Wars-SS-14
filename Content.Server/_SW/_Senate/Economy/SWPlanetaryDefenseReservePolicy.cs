using System.Linq;
using Content.Server._SW._Senate.Economy.Simulation;

namespace Content.Server._SW._Senate.Economy;

/// <summary>Finite shortage-hours, advanced once per immutable economic publication, never per UI frame.</summary>
public sealed class SWPlanetaryDefenseReservePolicy
{
    public const double CapacityHours = 4;
    private sealed class Account
    {
        public string? Pending;
        public double Hours;
        public double FundedDemandScale = 1;
    }
    private readonly Dictionary<string, Account> _accounts = new();
    private readonly Dictionary<string, float> _readiness = new();
    private double? _lastHour;

    public double Hours(string world, double demandScale = 1) =>
        _accounts.TryGetValue(world, out var account) ? account.Hours / LoadRatio(account, demandScale) : 0;
    public string? Pending(string world) => _accounts.GetValueOrDefault(world)?.Pending;
    public bool CanQueue(string world) => Hours(world) <= 1e-9 && Pending(world) == null;
    public float Readiness(string world, float supplied) => _readiness.GetValueOrDefault(world, supplied);

    public bool TryQueue(string world, string id, double fundedDemandScale = 1)
    {
        if (!CanQueue(world)) return false;
        if (!_accounts.TryGetValue(world, out var account)) _accounts[world] = account = new Account();
        account.Pending = id;
        account.FundedDemandScale = Math.Max(1, fundedDemandScale);
        return true;
    }

    public void Update(double hour, IReadOnlyDictionary<string, EconomyProjectState> projects,
        IReadOnlyDictionary<string, float> completedWorldReadiness,
        IReadOnlyDictionary<string, double>? demandScales = null)
    {
        if (_lastHour is { } previous && hour < previous) return;
        var elapsed = _lastHour is { } last ? Math.Max(0, hour - last) : 0;
        _lastHour = hour;
        _readiness.Clear();
        foreach (var world in _accounts.Keys.Union(completedWorldReadiness.Keys))
        {
            _accounts.TryGetValue(world, out var account);
            var window = elapsed;
            if (account?.Pending is { } id && projects.TryGetValue(id, out var project)
                && project.CompletedHour is { } completed && completed <= hour)
            {
                account.Pending = null;
                account.Hours = CapacityHours;
                window = Math.Min(window, Math.Max(0, hour - completed));
            }
            if (!completedWorldReadiness.TryGetValue(world, out var supplied))
            {
                _readiness[world] = 0;
                continue;
            }
            supplied = Math.Clamp(supplied, 0, 1);
            var ratio = account != null ? LoadRatio(account, demandScales?.GetValueOrDefault(world, 1) ?? 1) : 1;
            var covered = Math.Min(account?.Hours ?? 0, (1 - supplied) * window * ratio);
            if (account != null) account.Hours = Math.Max(0, account.Hours - covered);
            _readiness[world] = account?.Hours > 1e-9 ? 1f
                : Math.Clamp(supplied + (window > 1e-9 ? (float) (covered / (window * ratio)) : 0), 0, 1);
        }
    }

    public void Reset()
    {
        _accounts.Clear();
        _readiness.Clear();
        _lastHour = null;
    }

    private static double LoadRatio(Account account, double demandScale) => Math.Max(1, demandScale / account.FundedDemandScale);
}
