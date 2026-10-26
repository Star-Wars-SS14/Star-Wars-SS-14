using Content.Server.NPC.Events;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server._SW.Droids;

public sealed class SWDroidDoorNavigationSystem : EntitySystem
{
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedDoorSystem _door = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SWDroidDoorNavigationComponent, NPCSteeringEvent>(OnSteering);
    }

    private void OnSteering(Entity<SWDroidDoorNavigationComponent> ent, ref NPCSteeringEvent args)
    {
        if (_timing.CurTime < ent.Comp.NextDoorOpenAttempt)
            return;

        ent.Comp.NextDoorOpenAttempt = _timing.CurTime + ent.Comp.DoorOpenAttemptDelay;

        if (!TryFindNearbyClosedDoor(args.Transform.Coordinates, ent.Comp.DoorOpenRange, out var doorEnt))
            return;

        _door.TryOpen(doorEnt.Owner, doorEnt.Comp, ent.Owner, quiet: true);
    }

    private bool TryFindNearbyClosedDoor(EntityCoordinates coordinates, float range, out Entity<DoorComponent> door)
    {
        door = default;
        var closestDistance = float.MaxValue;

        foreach (var candidate in _lookup.GetEntitiesInRange<DoorComponent>(
                     coordinates,
                     range,
                     LookupFlags.Static | LookupFlags.Dynamic))
        {
            if (candidate.Comp.State is not (DoorState.Closed or DoorState.Denying) ||
                !coordinates.TryDistance(EntityManager, Transform(candidate.Owner).Coordinates, out var distance) ||
                distance >= closestDistance)
            {
                continue;
            }

            door = candidate;
            closestDistance = distance;
        }

        return door != default;
    }
}
