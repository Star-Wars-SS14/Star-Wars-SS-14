using System.Linq;
using Content.Shared._SW.GlobalWar;

namespace Content.Server._SW.GlobalWar.Market;

public sealed partial class SWGlobalWarMarketSimulation
{
    private readonly Dictionary<SWGlobalWarFaction, SWGlobalWarSupplyProgram> _supplyPrograms = new()
    {
        [SWGlobalWarFaction.Republic] = new(), [SWGlobalWarFaction.Empire] = new(),
    };
    internal Func<SWGlobalWarFaction, int>? SupplyParticipants;
    internal Func<SWGlobalWarFaction, SWGlobalWarSupplyKind, int>? AcceptedSupplyStock;
    internal Action? RunSupplyPrograms;
    public IReadOnlyDictionary<SWGlobalWarFaction, SWGlobalWarSupplyProgram> SupplyPrograms => _supplyPrograms;

    /// <summary>Captures starting readiness once; late arrivals never grant a second starting arsenal.</summary>
    internal void CaptureStartingSupply()
    {
        foreach (var (faction, program) in _supplyPrograms)
        {
            if (program.StartParticipants != null)
                continue;
            program.Participants = Math.Max(0, SupplyParticipants?.Invoke(faction) ?? 0);
            program.StartParticipants = Math.Clamp(program.Participants, 10, 30);
            program.StartArsenalLimit = (int) decimal.Ceiling(1.25m * program.StartParticipants.Value);
        }
    }

    private void StepSupplyPrograms()
    {
        var window = (int) (Time.Ticks / TimeSpan.FromMinutes(20).Ticks);
        foreach (var (faction, program) in _supplyPrograms)
        {
            program.Participants = Math.Max(0, SupplyParticipants?.Invoke(faction) ?? 0);
            if (window == 0 || window <= program.Window)
                continue;
            program.Window = window;
            var budgets = new Dictionary<SWGlobalWarSupplyKind, int>();
            foreach (var kind in Enum.GetValues<SWGlobalWarSupplyKind>())
            {
                var accepted = Math.Max(0, AcceptedSupplyStock?.Invoke(faction, kind) ?? 0);
                var outstanding = program.Orders.Values.Where(o => o.Kind == kind).Sum(o => (long) o.Outstanding);
                var deficit = Math.Max(0L, SWGlobalWarSupplyProgram.Target(kind, program.Participants) - accepted - outstanding);
                budgets.Add(kind, (int) Math.Min(SWGlobalWarSupplyProgram.Quota(kind, program.Participants), deficit));
            }
            program.Windows.Add(window, new(window, program.Participants, budgets));
        }
    }

    /// <summary>
    /// The transactional system calls this only after actual money and goods/material reservations succeed.
    /// Production, finished goods and transit remain stages of this same obligation.
    /// </summary>
    internal bool AddSupplyObligation(SWGlobalWarFaction faction, SWGlobalWarSupplyObligation order)
    {
        if (!_supplyPrograms.TryGetValue(faction, out var program) || order.Id == Guid.Empty || order.Quantity <= 0 ||
            order.Accepted != 0 || order.Cancelled != 0 || order.Window != program.Window ||
            program.RemainingQuota(order.Kind) < order.Quantity || program.Orders.ContainsKey(order.Id))
            return false;
        program.Orders.Add(order.Id, order);
        return true;
    }

    internal decimal EffectiveSupplyRate(SWGlobalWarFaction faction, SWGlobalWarSupplyKind kind)
    {
        var rate = _supplyPrograms[faction].Lines[kind].PerMinute;
        var doctrine = _lanthanumDoctrines[faction];
        if (doctrine.StartedAt == null)
            return rate;
        return kind switch
        {
            SWGlobalWarSupplyKind.T1 or SWGlobalWarSupplyKind.AdvancedElectronics => 0,
            SWGlobalWarSupplyKind.T2 => Math.Min(rate, EmergencyT2Rate(doctrine.CivilElectronics)),
            _ => rate,
        };
    }
}

/// <summary>Planning metadata only. Actual stock stays in registered physical stores and market batches.</summary>
public sealed class SWGlobalWarSupplyProgram
{
    public int Participants { get; internal set; }
    public int? StartParticipants { get; internal set; }
    public int StartArsenalLimit { get; internal set; }
    public int Window { get; internal set; }
    public Dictionary<int, SWGlobalWarSupplyWindow> Windows { get; } = new();
    public Dictionary<Guid, SWGlobalWarSupplyObligation> Orders { get; } = new();
    public Dictionary<SWGlobalWarSupplyKind, SWGlobalWarSupplyLine> Lines { get; } = new()
    {
        [SWGlobalWarSupplyKind.T2] = new(0.5m), [SWGlobalWarSupplyKind.T1] = new(0.125m),
        [SWGlobalWarSupplyKind.Armor] = new(0.5m), [SWGlobalWarSupplyKind.Magazine] = new(1),
        [SWGlobalWarSupplyKind.Ration] = new(4), [SWGlobalWarSupplyKind.Medicine] = new(2m / 3),
        [SWGlobalWarSupplyKind.AdvancedElectronics] = new(0.1m),
    };

    public int RemainingQuota(SWGlobalWarSupplyKind kind)
    {
        if (!Windows.TryGetValue(Window, out var decision))
            return 0;
        var used = Orders.Values.Where(o => o.Window == Window && o.Kind == kind)
            .Sum(o => (long) o.Accepted + o.Outstanding);
        return (int) Math.Max(0, decision.Budgets[kind] - used);
    }

    public static int Target(SWGlobalWarSupplyKind kind, int participants)
    {
        var n = Math.Max(0, participants);
        return checked((int) decimal.Ceiling(kind switch
        {
            SWGlobalWarSupplyKind.T2 or SWGlobalWarSupplyKind.Armor => 1.5m * n,
            SWGlobalWarSupplyKind.T1 => n / 3m,
            SWGlobalWarSupplyKind.Magazine => 3m * n,
            SWGlobalWarSupplyKind.Ration => 4m * n,
            SWGlobalWarSupplyKind.Medicine => 2m * n,
            SWGlobalWarSupplyKind.AdvancedElectronics => 20,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        }));
    }

    public static int Quota(SWGlobalWarSupplyKind kind, int participants)
    {
        var n = Math.Max(0, participants);
        return checked((int) decimal.Ceiling(kind switch
        {
            SWGlobalWarSupplyKind.T2 or SWGlobalWarSupplyKind.Armor => 0.2m * n,
            SWGlobalWarSupplyKind.Magazine => 0.6m * n,
            SWGlobalWarSupplyKind.Ration => 1.5m * n,
            SWGlobalWarSupplyKind.Medicine => 0.4m * n,
            _ => 0,
        }));
    }

    public static int UnitPrice(SWGlobalWarSupplyKind kind, bool bacta = false)
    {
        return kind switch
        {
            SWGlobalWarSupplyKind.T2 => 1500, SWGlobalWarSupplyKind.T1 => 600,
            SWGlobalWarSupplyKind.Armor => 1000, SWGlobalWarSupplyKind.Magazine => 200,
            SWGlobalWarSupplyKind.Ration => 50, SWGlobalWarSupplyKind.Medicine => bacta ? 500 : 150,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }
}

public sealed record SWGlobalWarSupplyWindow(int Index, int Participants,
    IReadOnlyDictionary<SWGlobalWarSupplyKind, int> Budgets);

public sealed class SWGlobalWarSupplyLine(decimal perMinute)
{
    public decimal PerMinute { get; internal set; } = perMinute;
    public int LocalPercent { get; internal set; } = 25;
    public decimal MarketRemainder { get; internal set; }
    public bool Bacta { get; internal set; }
    public long Revision { get; internal set; }
}

/// <summary>One funded quantity throughout manufacture, transport and partial acceptance; not another stock.</summary>
public sealed class SWGlobalWarSupplyObligation(Guid id, int window, SWGlobalWarSupplyKind kind, int quantity)
{
    public Guid Id { get; } = id;
    public int Window { get; } = window;
    public SWGlobalWarSupplyKind Kind { get; } = kind;
    public int Quantity { get; } = quantity;
    public int Accepted { get; internal set; }
    public int Cancelled { get; internal set; }
    public int Outstanding => Quantity - Accepted - Cancelled;
    public SWGlobalWarSupplyStage Stage { get; internal set; }
}
