using System.Linq;
using Content.Server._SW.GlobalWar.Production;
using Content.Shared._SW.GlobalWar;
using Robust.Shared.Containers;

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

        var query = AllEntityQuery<SWGlobalWarFactoryComponent>();
        while (query.MoveNext(out var uid, out var factory))
        {
            if (!factory.Arsenal || !BindFactory((uid, factory), out var owner) ||
                !_hubPlanets.TryGetPhysicalLocation(uid, out var planet, out _) ||
                _prototypes.Index(Comp<SWGlobalWarPlanetComponent>(planet).Prototype) is not
                    { Kind: SWGlobalWarSectorKind.Capital } world || world.InitialOwner != owner ||
                !TryGetSupplyStore(generation, uid, owner, out _, out var container))
                continue;

            var participants = Math.Clamp(_identity.CountSupplyParticipants(generation, owner), StartingKitMinimum,
                StartingKitMaximum);
            var kits = (int) Math.Ceiling(StartingKitsPerParticipant * participants);
            var stocked = _startingArsenals.GetValueOrDefault((generation, owner));
            if (kits <= stocked)
                continue;
            _startingArsenals[(generation, owner)] = kits;
            var source = Guid.NewGuid();
            for (var i = stocked; i < kits; i++)
            {
                foreach (var (kind, count) in StartingKit)
                {
                    for (var j = 0; j < count; j++)
                        AddStartingItem(uid, container, generation, owner, source, kind);
                }
            }
        }
    }

    private void AddStartingItem(EntityUid store, BaseContainer container, Guid generation, SWGlobalWarFaction owner,
        Guid source, SWGlobalWarSupplyKind kind)
    {
        var item = Spawn(SupplyPrototype(kind, owner), Transform(store).Coordinates);
        // Rifles are issued empty: magazines are counted and drawn separately.
        if (_supplyContainers.TryGetContainer(item, "gun_magazine", out var magazine))
        {
            foreach (var loaded in magazine.ContainedEntities.ToArray())
                Del(loaded);
        }

        if (!_supplyContainers.Insert(item, container))
        {
            Del(item);
            return;
        }

        _supplyItems.Add(item, new(generation, kind, false, owner, owner, SWGlobalWarSupplyOrigin.Starting, source,
            Guid.NewGuid(), Accepted: true));
    }
}
