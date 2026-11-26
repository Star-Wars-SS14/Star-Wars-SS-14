using System.Linq;
using System.Numerics;
using Content.Server._SW.GlobalWar.Production;
using Content.Shared._SW.GlobalWar;
using Content.Shared.Conveyor;
using Content.Server.Power.EntitySystems;
using Robust.Shared.Maths;

namespace Content.Server._SW.GlobalWar.Market;

public sealed partial class SWGlobalWarMarketSystem
{
    private const float MiningConveyorRange = 1.5f;
    private static readonly TimeSpan PhysicalMiningInterval = TimeSpan.FromSeconds(1);
    private readonly Dictionary<string, HashSet<EntityUid>> _physicalMiningSites = new();

    private bool CanRunPhysicalMine(Entity<SWGlobalWarFactoryComponent> ent, SWGlobalWarFaction owner,
        out string world, out string commodity)
    {
        world = commodity = string.Empty;
        if (!ent.Comp.Mining || !TryGetTerminalMarket(ent, out var market) || market.Owner != owner ||
            market is not { Kind: SWGlobalWarSectorKind.Mining, Profile: { } profile })
            return false;
        world = market.Planet;
        commodity = profile.MiningResource?.Id ?? string.Empty;
        if (commodity.Length == 0)
            return false;
        if (!_physicalMiningSites.TryGetValue(world, out var sites))
            _physicalMiningSites[world] = sites = new();
        sites.RemoveWhere(uid => TerminatingOrDeleted(uid) || !Transform(uid).Anchored ||
            !TryGetTerminalMarket(uid, out var site) || site.Planet != market.Planet || site.Owner != owner);
        if (!sites.Contains(ent) && sites.Count >= 2)
            return false;
        if (FindMiningConveyor(ent) == null)
            return false;
        sites.Add(ent);
        return true;
    }

    /// <summary>Only local, anchored belts on this grid can receive ore. The outlet faces the selected belt.</summary>
    private EntityUid? FindMiningConveyor(EntityUid drill)
    {
        var transform = Transform(drill);
        if (transform.GridUid == null)
            return null;
        var position = _supplyTransforms.GetWorldPosition(drill);
        EntityUid? closest = null;
        var distance = MiningConveyorRange * MiningConveyorRange;
        // A sleeping planet must still find its belt before acquiring a work lease to wake the surface.
        var query = EntityManager.AllEntityQueryEnumerator<ConveyorComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var belt))
        {
            if (TerminatingOrDeleted(uid) || !belt.Anchored || belt.GridUid != transform.GridUid)
                continue;
            var offset = _supplyTransforms.GetWorldPosition(uid) - position;
            var squared = offset.LengthSquared();
            // A belt underneath the drill has no visible outlet direction.
            if (squared < 0.25f || squared > distance || closest is { } current && squared == distance && uid.Id >= current.Id)
                continue;
            distance = squared;
            closest = uid;
        }
        if (closest is { } target)
        {
            var direction = _supplyTransforms.GetWorldPosition(target) - position;
            // The dispenser sprite's outlet points down in its unrotated state.
            _supplyTransforms.SetWorldRotation(drill, direction.ToAngle() + Angle.FromDegrees(90));
        }
        return closest;
    }

    private string MiningDescription(Entity<SWGlobalWarFactoryComponent> ent, bool valid)
    {
        var resource = Loc.GetString("sw-global-war-mining-no-resource");
        if (TryGetTerminalMarket(ent, out var market) && market.Kind == SWGlobalWarSectorKind.Mining &&
            market.Profile?.MiningResource is { } commodity)
            resource = Loc.GetString(_prototypes.Index(commodity).Name);
        var state = !Transform(ent).Anchored ? "anchor" : !valid ? "unavailable" :
            !ent.Comp.Enabled ? "stopped" : !this.IsPowered(ent, EntityManager) ? "power" :
            market == null || market.Kind != SWGlobalWarSectorKind.Mining ? "planet" :
            FindMiningConveyor(ent) == null ? "conveyor" :
            !CanRunPhysicalMine(ent, market.Owner, out _, out _) ? "limit" : "working";
        return Loc.GetString("sw-global-war-mining-details", ("resource", resource),
            ("state", Loc.GetString("sw-global-war-mining-state-" + state)));
    }

    private void AdvancePhysicalMine(Entity<SWGlobalWarFactoryComponent> ent, SWGlobalWarFaction owner,
        string world, string commodity, TimeSpan elapsed)
    {
        if (FindMiningConveyor(ent) is not { } conveyor)
            return;
        ent.Comp.MiningTime += elapsed;
        if (ent.Comp.MiningTime < PhysicalMiningInterval)
            return;
        Spawn(_prototypes.Index<SWGlobalWarCommodityPrototype>(commodity).Entity, Transform(conveyor).Coordinates);
        ent.Comp.MiningTime -= PhysicalMiningInterval;
    }
}
