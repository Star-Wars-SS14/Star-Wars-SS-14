using System.Linq;
using System.Numerics;
using Content.Server._SW.GlobalWar.Production;
using Content.Shared._SW.GlobalWar;
using Robust.Shared.Map;

namespace Content.Server._SW.GlobalWar.Market;

public sealed partial class SWGlobalWarMarketSystem
{
    private const int PhysicalMiningDeposit = 2400;
    private static readonly TimeSpan PhysicalMiningInterval = TimeSpan.FromSeconds(10);
    private readonly Dictionary<string, int> _physicalMiningDeposits = new();
    private readonly Dictionary<string, HashSet<EntityUid>> _physicalMiningSites = new();

    private bool CanRunPhysicalMine(Entity<SWGlobalWarFactoryComponent> ent, SWGlobalWarFaction owner,
        out string world, out string commodity)
    {
        world = commodity = string.Empty;
        if (!ent.Comp.Mining || !TryGetSupplyStore(ent.Comp.Generation, ent, owner, out var binding, out var inventory) ||
            inventory.ContainedEntities.Count >= 600 || Simulation!.Markets[binding.Planet] is not
                { Kind: SWGlobalWarSectorKind.Mining, Profile: { } profile })
            return false;
        world = binding.Planet;
        commodity = profile.Recipes.FirstOrDefault(r => r.Inputs.Count == 0 && r.Output.Id is
            "SWGlobalWarFe" or "SWGlobalWarC" or "SWGlobalWarAu" or "SWGlobalWarLn")?.Output.Id ?? string.Empty;
        if (commodity.Length == 0)
            return false;
        if (!_physicalMiningSites.TryGetValue(world, out var sites))
            _physicalMiningSites[world] = sites = new();
        var generation = ent.Comp.Generation;
        sites.RemoveWhere(uid => TerminatingOrDeleted(uid) || !Transform(uid).Anchored ||
            !TryGetSupplyStore(generation, uid, owner, out var site, out _) || site.Planet != binding.Planet);
        if (!sites.Contains(ent) && sites.Count >= 2)
            return false;
        sites.Add(ent);
        _physicalMiningDeposits.TryAdd(world, PhysicalMiningDeposit);
        return ent.Comp.MiningOutput != null || _physicalMiningDeposits[world] > 0;
    }

    private void AdvancePhysicalMine(Entity<SWGlobalWarFactoryComponent> ent, SWGlobalWarFaction owner,
        string world, string commodity, TimeSpan elapsed)
    {
        if (!TryGetSupplyStore(ent.Comp.Generation, ent, owner, out _, out var inventory))
            return;
        if (ent.Comp.MiningOutput == null)
        {
            ent.Comp.MiningTime += elapsed;
            if (ent.Comp.MiningTime < PhysicalMiningInterval || _physicalMiningDeposits.GetValueOrDefault(world) <= 0)
                return;
            ent.Comp.MiningEscrow ??= CreateSupplyEscrow();
            var item = Spawn(_prototypes.Index<SWGlobalWarCommodityPrototype>(commodity).Entity,
                new EntityCoordinates(ent.Comp.MiningEscrow.Value, Vector2.Zero));
            // This is the separate finite physical deposit from ECON, not the market's abstract production or Ln reserve.
            _physicalMiningDeposits[world]--;
            ent.Comp.MiningTime -= PhysicalMiningInterval;
            ent.Comp.MiningOutput = item;
        }
        if (_supplyContainers.Insert(ent.Comp.MiningOutput.Value, inventory))
            ent.Comp.MiningOutput = null;
    }
}
