using Content.Server._SW.Lathe.MagnetPickup;
using Content.Server.Power.EntitySystems;
using Content.Server.Stack;
using Content.Server.Storage.EntitySystems;
using Content.Shared.Stacks;
using Content.Shared.Whitelist;
using Robust.Server.Containers;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.Lathe.OreDispenser;
public sealed class SWOreDispenserSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelist = default!;
    [Dependency] private readonly ContainerSystem _containerSystem = default!;
    [Dependency] private readonly TransformSystem _transformSystem = default!;
    [Dependency] private readonly StackSystem _stackSystem = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly PowerChargeSystem _chargeSystem = default!;
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<SWOreDispenserComponent, ContainerManagerComponent>();

        while (query.MoveNext(out var uid, out var comp, out var containerComp)) {
            if (!_chargeSystem.IsPowered(uid, EntityManager))
            {
                continue;
            }

            if (_timing.CurTime > comp.NextDispense)
            {
                var container = containerComp.Containers[comp.ContainerName];
                var items = container.ContainedEntities;

                foreach (var item in items)
                {
                    if (_whitelist.IsWhitelistPass(comp.DispenseWhitelist, item)){
                        DispenseItem(new Entity<SWOreDispenserComponent>(uid, comp), container, item);
                        comp.NextDispense = _timing.CurTime + TimeSpan.FromSeconds(comp.DelayBetweenSpawns);
                    }
                }
            }
        }

    }

    private void DispenseItem(Entity<SWOreDispenserComponent> ent, BaseContainer container, EntityUid item)
    {
        var ent_coords = new EntityCoordinates(ent, ent.Comp.SpawnPointOut);
        var world_coords = _transformSystem.ToWorldPosition(ent_coords);

        EntityUid result;
        if (!TryComp<StackComponent>(item, out var stackComp))
        {
            _containerSystem.Remove(item, container);
            _transformSystem.SetWorldPosition(item, world_coords);
            result = item;
        }
        else
        {
            var stackTypeProto = _prototypeManager.Index(stackComp.StackTypeId);
            _stackSystem.ReduceCount(item, 1);
            result = Spawn(stackTypeProto.Spawn, _transformSystem.ToMapCoordinates(ent_coords));
        }
        var comp = AddComp<SWMagnetUnpickupableComponent>(result);
        comp.DespawnTime = _timing.CurTime + TimeSpan.FromSeconds(1);
    }
}
