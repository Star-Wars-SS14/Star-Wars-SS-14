using Content.Shared._SW.GlobalWar;

namespace Content.Server._SW.GlobalWar.Market;

/// <summary>Marks a campaign whose capitals stock a finite arsenal, sized by the most participants a side has had.</summary>
[RegisterComponent]
public sealed partial class SWGlobalWarStartingArsenalComponent : Component;

public sealed partial class SWGlobalWarMarketSystem
{
    private const int StartingKitMinimum = 10;
    private const int StartingKitMaximum = 30;
    // With no industry player this is all a side gets but rations, so it has to outlast about 100 minutes of war.
    private const double StartingKitsPerParticipant = 5;

    /// <summary>One issue limit's worth of each item, so every kit can be drawn in full by one soldier.</summary>
    private static readonly (SWGlobalWarSupplyKind Kind, int Count)[] StartingKit =
    [
        (SWGlobalWarSupplyKind.T2, 1),
        (SWGlobalWarSupplyKind.Armor, 1),
        (SWGlobalWarSupplyKind.Magazine, 3),
        (SWGlobalWarSupplyKind.Ration, 2),
        (SWGlobalWarSupplyKind.Medicine, 2),
    ];

    private readonly Dictionary<(Guid Generation, SWGlobalWarFaction Faction), int> _startingArsenals = new();
    private TimeSpan _nextStartingArsenal;

    /// <summary>
    /// Most players ready up after the first spawn, so a side that grows gets the difference, once and only upward.
    /// A captured capital stocks nobody: the kits belong to the side that started there.
    /// </summary>
    private void UpdateStartingArsenals()
    {
        if (Simulation?.Generation is not { } generation || !HasComp<SWGlobalWarStartingArsenalComponent>(_rule) ||
            _factoryTiming.CurTime < _nextStartingArsenal && _startingArsenals.Count == 2)
            return;
        _nextStartingArsenal = _factoryTiming.CurTime + TimeSpan.FromSeconds(5);

        foreach (var market in Simulation.Markets.Values)
        {
            var owner = market.Owner;
            if (market.Kind != SWGlobalWarSectorKind.Capital || market.OriginalOwner != owner ||
                owner is not (SWGlobalWarFaction.Republic or SWGlobalWarFaction.Empire))
                continue;
            var participants = Math.Clamp(_identity.CountSupplyParticipants(generation, owner), StartingKitMinimum,
                StartingKitMaximum);
            var kits = (int) Math.Ceiling(StartingKitsPerParticipant * participants);
            var stocked = _startingArsenals.GetValueOrDefault((generation, owner));
            if (kits <= stocked)
                continue;
            foreach (var (kind, count) in StartingKit)
                Simulation.DepositMarketStock(market.Planet, SupplyCommodity(kind, false),
                    (kits - stocked) * count * Simulation.UnitsPerItem);
            _startingArsenals[(generation, owner)] = kits;
        }
    }
}
