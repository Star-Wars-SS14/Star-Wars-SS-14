using System.Linq;
using System.Numerics;
using Content.Server._SW.Targeting;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Server.Spawners.Components;
using Content.Server.Station.Systems;
using Content.Shared._SW.TaskSystem;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Maps;
using Content.Shared.Mobs.Systems;
using Content.Shared.Physics;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._SW.DesignTesting.Bots;

public sealed class SWCombatBotSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly StationSpawningSystem _spawning = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedWieldableSystem _wieldable = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly TurfSystem _turf = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly HTNSystem _htn = default!;
    [Dependency] private readonly NPCSystem _npc = default!;

    /// <summary>
    /// The NPC blackboard key the bot HTN walks to.
    /// </summary>
    public const string ObjectiveKey = "SWBotObjective";

    private const string MagazineSlot = "gun_magazine";
    private const string TowerPrototype = "SWCommunicationTower";

    /// <summary>
    /// The objective moves less than this before the bot replans its walk.
    /// </summary>
    private const float ReplanDistance = 3f;

    /// <summary>
    /// The NPC pathfinder gives up after 512 nodes, so a walk across the map is split into legs this long.
    /// </summary>
    private const float WaypointStep = 20f;

    /// <summary>
    /// Each leg turns up to this many degrees off the straight line, so a bot blocked by a wall tries another way.
    /// </summary>
    private const float WaypointSpreadDegrees = 35f;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWCombatBotComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<SWCombatBotComponent> ent, ref MapInitEvent args)
    {
        var job = _proto.Index(ent.Comp.Job);

        var member = EnsureComp<SWFactionMemberComponent>(ent);
        member.Faction = ent.Comp.Faction;
        member.Job = job.ID;
        Dirty(ent, member);

        _spawning.EquipStartingGear(ent, job.StartingGear);
        SetupTargeting(ent);

        ent.Comp.NextThink = _timing.CurTime + ent.Comp.ThinkInterval;
    }

    /// <summary>
    /// Moves the first gun of the kit into the hands and wields it, and remembers its magazine for reloads.
    /// </summary>
    private void ArmGun(Entity<SWCombatBotComponent> ent)
    {
        var slots = _inventory.GetSlotEnumerator(ent.Owner);
        while (slots.NextItem(out var item, out var slot))
        {
            if (!HasComp<GunComponent>(item))
                continue;

            if (!_inventory.TryUnequip(ent, slot.Name, silent: true, force: true)
                || !_hands.TryPickupAnyHand(ent, item, checkActionBlocker: false, animate: false))
            {
                continue;
            }

            ent.Comp.Gun = item;
            if (TryComp<WieldableComponent>(item, out var wieldable))
                _wieldable.TryWield(item, wieldable, ent);

            if (_container.TryGetContainer(item, MagazineSlot, out var magazines)
                && magazines.ContainedEntities.FirstOrDefault() is { Valid: true } magazine
                && MetaData(magazine).EntityPrototype is { } magazineProto)
            {
                ent.Comp.MagazinePrototype = magazineProto.ID;
            }
            return;
        }
    }

    /// <summary>
    /// Makes the auto-targeting gun spare every job of the bot's own faction and shoot everyone else.
    /// </summary>
    private void SetupTargeting(Entity<SWCombatBotComponent> ent)
    {
        var config = EnsureComp<SWTargetingConfigComponent>(ent);
        config.AllowedJobs.Clear();
        config.TargetUnconfiguredJobs = true;
        config.TargetHumanoidsWithoutId = false;
        config.TargetAnimals = false;

        var factionProto = _proto.Index(ent.Comp.Faction);
        if (factionProto.TryGetComponent<SWFactionComponent>(out var faction, EntityManager.ComponentFactory))
        {
            foreach (var job in faction.RelatedJobs)
            {
                config.AllowedJobs[job.Id] = true;
            }
        }
    }

    /// <summary>
    /// Spawns bots around a point. Bot prototypes are picked by their <see cref="SWCombatBotComponent.Faction"/>.
    /// </summary>
    public List<EntityUid> SpawnBots(string faction,
        int count,
        MapCoordinates at,
        SWBotObjective objective,
        MapCoordinates? point = null)
    {
        var spawned = new List<EntityUid>();
        var proto = GetBotPrototype(faction);
        if (proto == null)
            return spawned;

        var spot = FindFreeSpot(at);
        for (var i = 0; i < count; i++)
        {
            var bot = SpawnAtPosition(proto, spot.Offset(_random.NextVector2(0.3f)));
            var comp = Comp<SWCombatBotComponent>(bot);
            comp.Objective = objective;
            comp.Point = point;
            comp.NextObjectiveUpdate = TimeSpan.Zero;
            spawned.Add(bot);
        }
        return spawned;
    }

    /// <summary>
    /// The centre of the nearest walkable tile, so bots are not spawned inside a console or a wall.
    /// </summary>
    private EntityCoordinates FindFreeSpot(MapCoordinates at)
    {
        const int maxRadius = 6;
        if (!_mapManager.TryFindGridAt(at, out var gridUid, out var grid))
            return _transform.ToCoordinates(at);

        var centre = _map.TileIndicesFor(gridUid, grid, at);
        for (var radius = 0; radius <= maxRadius; radius++)
        {
            for (var x = -radius; x <= radius; x++)
            {
                for (var y = -radius; y <= radius; y++)
                {
                    if (Math.Max(Math.Abs(x), Math.Abs(y)) != radius)
                        continue;

                    var indices = centre + new Vector2i(x, y);
                    if (!_map.TryGetTileRef(gridUid, grid, indices, out var tile)
                        || tile.Tile.IsEmpty
                        || _turf.IsTileBlocked(gridUid, indices, CollisionGroup.Impassable, grid))
                    {
                        continue;
                    }

                    return _map.GridTileToLocal(gridUid, grid, indices);
                }
            }
        }

        return _transform.ToCoordinates(at);
    }

    /// <summary>
    /// The bot entity prototype that fights for the faction, or null if there is none.
    /// </summary>
    public string? GetBotPrototype(string faction)
    {
        foreach (var proto in _proto.EnumeratePrototypes<EntityPrototype>())
        {
            if (!proto.Abstract
                && proto.TryGetComponent<SWCombatBotComponent>(out var bot, EntityManager.ComponentFactory)
                && bot.Faction == faction)
            {
                return proto.ID;
            }
        }
        return null;
    }

    /// <summary>
    /// True while the bot's auto-targeting gun has an enemy in sight.
    /// </summary>
    public bool IsEngaged(EntityUid bot)
    {
        return TryComp<SWAutoTargetingGunComponent>(bot, out var gun)
               && gun.Target != null
               && gun.TargetInLineOfSight;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<SWCombatBotComponent, HTNComponent>();
        while (query.MoveNext(out var uid, out var bot, out var htn))
        {
            if (now < bot.NextThink)
                continue;

            bot.NextThink = now + bot.ThinkInterval;
            if (!_mobState.IsAlive(uid))
                continue;

            // Hands come from body parts, which do not exist yet when the bot's own MapInit runs.
            if (!bot.Armed)
            {
                bot.Armed = true;
                ArmGun((uid, bot));
            }

            Reload((uid, bot), now);

            if (now >= bot.NextObjectiveUpdate)
            {
                bot.NextObjectiveUpdate = now + bot.ObjectiveInterval;
                bot.ObjectivePosition = GetObjectivePosition((uid, bot));
                bot.Waypoint = NextWaypoint(uid, bot.ObjectivePosition);
            }
            else if (bot.Waypoint is { } waypoint && IsNear(uid, waypoint))
            {
                bot.Waypoint = NextWaypoint(uid, bot.ObjectivePosition);
            }

            var target = IsEngaged(uid) ? null : bot.Waypoint;
            UpdateMovement((uid, bot), htn, target);
        }
    }

    private void Reload(Entity<SWCombatBotComponent> ent, TimeSpan now)
    {
        if (ent.Comp.Gun is not { } gun || ent.Comp.MagazinePrototype is not { } magazineProto)
            return;

        if (ent.Comp.ReloadEnd is { } end)
        {
            if (now < end)
                return;

            ent.Comp.ReloadEnd = null;
            if (!_container.TryGetContainer(gun, MagazineSlot, out var slot))
                return;

            foreach (var old in slot.ContainedEntities.ToArray())
            {
                Del(old);
            }

            var magazine = Spawn(magazineProto, Transform(ent).Coordinates);
            if (!_container.Insert(magazine, slot))
                Del(magazine);
            return;
        }

        var ammo = new GetAmmoCountEvent();
        RaiseLocalEvent(gun, ref ammo);
        if (ammo.Count == 0)
            ent.Comp.ReloadEnd = now + ent.Comp.ReloadTime;
    }

    private MapCoordinates? GetObjectivePosition(Entity<SWCombatBotComponent> ent)
    {
        switch (ent.Comp.Objective)
        {
            case SWBotObjective.EnemyBase:
                return GetBasePosition(ent.Comp.EnemyFaction);
            case SWBotObjective.Centre:
                var own = GetBasePosition(ent.Comp.Faction);
                var enemy = GetBasePosition(ent.Comp.EnemyFaction);
                if (own == null || enemy == null || own.Value.MapId != enemy.Value.MapId)
                    return enemy;
                return new MapCoordinates((own.Value.Position + enemy.Value.Position) / 2, own.Value.MapId);
            case SWBotObjective.Towers:
                return GetNearestTower(ent) ?? GetBasePosition(ent.Comp.EnemyFaction);
            case SWBotObjective.Point:
                return ent.Comp.Point;
            default:
                return null;
        }
    }

    private bool IsNear(EntityUid bot, MapCoordinates point)
    {
        var from = _transform.GetMapCoordinates(bot);
        return from.MapId == point.MapId && (from.Position - point.Position).Length() < ReplanDistance;
    }

    private MapCoordinates? NextWaypoint(EntityUid bot, MapCoordinates? objective)
    {
        if (objective is not { } goal)
            return null;

        var from = _transform.GetMapCoordinates(bot);
        if (from.MapId != goal.MapId)
            return null;

        var direction = goal.Position - from.Position;
        var distance = direction.Length();
        if (distance <= WaypointStep)
            return goal;

        var spread = MathHelper.DegreesToRadians(_random.NextFloat(-WaypointSpreadDegrees, WaypointSpreadDegrees));
        var step = new Angle(spread).RotateVec(direction / distance) * WaypointStep;
        return new MapCoordinates(from.Position + step, from.MapId);
    }

    /// <summary>
    /// Where a faction's players appear: the job spawn point of the faction closest to the centre of all of them.
    /// Falls back to the faction's task console.
    /// </summary>
    public MapCoordinates? GetBasePosition(string faction)
    {
        if (!_proto.TryIndex<EntityPrototype>(faction, out var factionProto)
            || !factionProto.TryGetComponent<SWFactionComponent>(out var factionComp, EntityManager.ComponentFactory))
        {
            return GetConsolePosition(faction);
        }

        var points = new List<MapCoordinates>();
        var query = EntityQueryEnumerator<SpawnPointComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var spawn, out var xform))
        {
            if (spawn.Job is { } job && factionComp.RelatedJobs.Contains(job))
                points.Add(_transform.GetMapCoordinates(uid, xform));
        }

        if (points.Count == 0)
            return GetConsolePosition(faction);

        var mapId = points[0].MapId;
        var sameMap = points.Where(p => p.MapId == mapId).ToList();
        var centre = sameMap.Aggregate(Vector2.Zero, (sum, p) => sum + p.Position) / sameMap.Count;
        return sameMap.MinBy(p => (p.Position - centre).LengthSquared());
    }

    private MapCoordinates? GetConsolePosition(string faction)
    {
        var query = EntityQueryEnumerator<TaskConsoleComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var console, out var xform))
        {
            if (console.Faction == faction)
                return _transform.GetMapCoordinates(uid, xform);
        }
        return null;
    }

    private MapCoordinates? GetNearestTower(Entity<SWCombatBotComponent> ent)
    {
        var from = _transform.GetMapCoordinates(ent);
        MapCoordinates? best = null;
        var bestDistance = float.MaxValue;
        var query = EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var meta, out var xform))
        {
            if (meta.EntityPrototype?.ID != TowerPrototype)
                continue;

            var pos = _transform.GetMapCoordinates(uid, xform);
            if (pos.MapId != from.MapId)
                continue;

            var distance = (pos.Position - from.Position).LengthSquared();
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = pos;
            }
        }
        return best;
    }

    /// <summary>
    /// Writes the walk target to the HTN blackboard and replans only when it really changed,
    /// so a running walk is not restarted every think.
    /// </summary>
    private void UpdateMovement(Entity<SWCombatBotComponent> ent, HTNComponent htn, MapCoordinates? target)
    {
        var current = ent.Comp.MovingTo;
        if (current == null && target == null)
            return;

        if (current != null && target != null && current.Value.MapId == target.Value.MapId
            && (current.Value.Position - target.Value.Position).Length() < ReplanDistance)
        {
            return;
        }

        ent.Comp.MovingTo = target;

        // Holding is a walk to where the bot stands. Removing the key instead would crash a plan that is still
        // being made in the background and reads it later.
        var xform = Transform(ent);
        var walkTo = target is { } coords && xform.GridUid is { } grid
            // MoveToOperator only plans to coordinates on a grid; on a planet map the ground is a grid in the map.
            ? _transform.ToCoordinates(grid, coords)
            : xform.Coordinates;
        _npc.SetBlackboard(ent, ObjectiveKey, walkTo, htn);

        htn.PlanningToken?.Cancel();
        htn.PlanningToken = null;
        htn.PlanningJob = null;
        if (htn.Plan != null)
            _htn.ShutdownPlan(htn);
        _htn.Replan(htn);
    }
}
