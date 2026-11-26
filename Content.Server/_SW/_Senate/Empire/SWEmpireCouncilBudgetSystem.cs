using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server._SW._Senate.Core;
using Content.Server._SW._Senate.Economy;
using Content.Server._SW._Senate.Economy.Simulation;
using Content.Shared._SW._Senate.Economy;
using Content.Shared._SW._Senate.Empire;
using Content.Shared.GameTicking;
using Robust.Shared.Prototypes;

namespace Content.Server._SW._Senate.Empire;

/// <summary>Finite independent Imperial treasury and sphere allowances.</summary>
public sealed class SWEmpireCouncilBudgetSystem : EntitySystem
{
    public const int InitialTreasury = 20000, InitialSphereAllowance = 6000, DailySphereAllocation = 1500;
    public const double IncomeRate = .01, MaxIncomePerHour = 2000;
    [Dependency] private readonly SWEmpireEconomySystem _economy = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    private readonly Dictionary<SWEmpireCouncilSphere, int> _balances = new();
    private readonly Dictionary<string, (SWEmpireCouncilSphere Sphere, int Amount)> _reservations = new();
    private readonly HashSet<string> _used = new();
    private readonly Dictionary<SWEmpireCouncilSphere, long> _spent = new(), _received = new();
    private double _lastHour = -1;
    private double _fraction;
    private int _allocationDay = -1;
    public long Treasury { get; private set; }
    public double IncomePerHour { get; private set; }
    public IReadOnlyDictionary<SWEmpireCouncilSphere, int> Balances => _balances;
    public IReadOnlyDictionary<SWEmpireCouncilSphere, long> Spent => _spent;
    public IReadOnlyDictionary<SWEmpireCouncilSphere, long> Received => _received;
    public long Allocated => _balances.Values.Sum(v => (long)v) + _reservations.Values.Sum(v => (long)v.Amount);
    public long TotalFunds => Treasury + Allocated;
    public int NextAllocationDay => _allocationDay + 1;
    public static readonly SWEmpireCouncilSphere[] Spheres = { SWEmpireCouncilSphere.ProductionLogistics,
        SWEmpireCouncilSphere.MilitaryOffense, SWEmpireCouncilSphere.Intelligence, SWEmpireCouncilSphere.ExpansionDiplomacy };

    public override void Initialize()
    {
        UpdatesAfter.Add(typeof(SWEmpireEconomySystem));
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => Reset()); Reset();
    }
    private void Reset()
    {
        _balances.Clear(); _spent.Clear(); _received.Clear();
        foreach (var sphere in Spheres) { _balances[sphere] = InitialSphereAllowance; _spent[sphere] = 0; _received[sphere] = 0; }
        Treasury = InitialTreasury; _reservations.Clear(); _used.Clear(); _lastHour = -1;
        _fraction = 0; _allocationDay = -1; IncomePerHour = 0;
    }
    public override void Update(float frameTime)
    {
        if (!_time.ModeActive || !_time.DayActive) return;
        if (_allocationDay != _time.Day)
        {
            _allocationDay = _time.Day;
            var distributable = Math.Min(Treasury, (long)DailySphereAllocation * Spheres.Length);
            var share = (int)(distributable / Spheres.Length);
            var remainder = (int)(distributable % Spheres.Length);
            for (var i = 0; i < Spheres.Length; i++)
            {
                var sphere = Spheres[i];
                var amount = share + (i < remainder ? 1 : 0);
                if (amount <= 0 || _balances[sphere] > int.MaxValue - amount) continue;
                Treasury -= amount; _balances[sphere] += amount; _received[sphere] += amount;
            }
        }
        var snapshot = _economy.Snapshot;
        if (snapshot == null || snapshot.Hour <= _lastHour) return;
        _lastHour = snapshot.Hour;
        var gross = snapshot.Goods.Sum(g => Math.Max(0, g.Produced) * Assessment(g.Good));
        var earned = Math.Min(MaxIncomePerHour * SWRepublicEconomyRunner.StepHours, gross * IncomeRate);
        IncomePerHour = earned / SWRepublicEconomyRunner.StepHours;
        _fraction += earned; var credit = (long)Math.Floor(_fraction);
        if (credit > 0) { Treasury = Math.Min(int.MaxValue, Treasury + credit); _fraction -= credit; }
    }
    private double Assessment(string id)
    {
        if (!_prototypes.TryIndex<SWCommodityPrototype>(id, out var good)) return 120;
        return good.Category.Id switch { "resources" => 60, "food" => 90, "materials" => 120,
            "energy" => 108, "components" => 180, "civil" => 150, "health" => 180,
            "defense" => 210, "culture" => 150, "controlled" => 210, _ => 120 };
    }
    private bool Ready(SWEmpireCouncilSphere sphere, string requestId) =>
        _time.ModeActive && _time.DayActive && _balances.ContainsKey(sphere) &&
        !string.IsNullOrWhiteSpace(requestId) && requestId.Length <= 128 && !_used.Contains(requestId);
    public bool TryReserve(SWEmpireCouncilSphere sphere, int amount, string requestId, out string reason)
    {
        reason = "Недоступно: проверьте время, сумму и повтор запроса";
        if (!Ready(sphere, requestId) || amount <= 0 || amount > _balances[sphere]) return false;
        _used.Add(requestId); _balances[sphere] -= amount;
        _reservations.Add(requestId, (sphere, amount)); reason = string.Empty; return true;
    }
    public bool Commit(SWEmpireCouncilSphere sphere, string requestId)
    {
        if (!_reservations.TryGetValue(requestId, out var held) || held.Sphere != sphere) return false;
        _spent[sphere] += held.Amount; return _reservations.Remove(requestId);
    }
    public bool HasReservation(SWEmpireCouncilSphere sphere, string requestId) =>
        _reservations.TryGetValue(requestId, out var held) && held.Sphere == sphere;
    public bool HasReservation(SWEmpireCouncilSphere sphere, string requestId, int amount) =>
        _reservations.TryGetValue(requestId, out var held) && held.Sphere == sphere && held.Amount == amount;
    /// <summary>Settle actual expenditure and release the unused part of a held reservation.</summary>
    public bool CompleteReservation(SWEmpireCouncilSphere sphere, string requestId, int spent)
    {
        if (!_reservations.TryGetValue(requestId, out var held) || held.Sphere != sphere || spent < 0 || spent > held.Amount ||
            _balances[sphere] > int.MaxValue - (held.Amount - spent)) return false;
        _balances[sphere] += held.Amount - spent;
        _spent[sphere] += spent;
        _reservations.Remove(requestId);
        return true;
    }
    public bool Refund(SWEmpireCouncilSphere sphere, string requestId)
    {
        if (!_reservations.TryGetValue(requestId, out var held) || held.Sphere != sphere || _balances[sphere] > int.MaxValue - held.Amount) return false;
        _balances[sphere] += held.Amount; _reservations.Remove(requestId); return true;
    }
    public bool Transfer(SWEmpireCouncilSphere source, SWEmpireCouncilSphere target, int amount, string requestId)
    {
        if (!Ready(source, requestId) || source == target || !_balances.ContainsKey(target) || amount <= 0 ||
            amount > _balances[source] || _balances[target] > int.MaxValue - amount) return false;
        _used.Add(requestId); _balances[source] -= amount; _balances[target] += amount; _received[target] += amount; return true;
    }
}

