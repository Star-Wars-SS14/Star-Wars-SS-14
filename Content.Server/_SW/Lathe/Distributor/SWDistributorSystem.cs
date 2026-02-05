using Content.Server._SW.Lathe.SWLathe;
using Content.Server.Power.EntitySystems;
using Content.Shared.Item;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics.Events;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.Lathe.Distributor;
public sealed class SWDistributorSystem : EntitySystem
{
    [Dependency] private readonly TransformSystem _transformSystem = default!;
    [Dependency] private readonly PowerChargeSystem _chargeSystem = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWDistributorComponent, StartCollideEvent>(OnStartCollide);
    }
    private void OnStartCollide(Entity<SWDistributorComponent> ent, ref StartCollideEvent args)
    {
        if (!_chargeSystem.IsPowered(ent, EntityManager))
        {
            return;
        }

        string fix = args.OurFixtureId;

        foreach (var info in ent.Comp.RedirectionInfo)
        {
            if (info.InputFixture == fix && IsEntityTransportable(args.OtherEntity))
            {
                var index = info.IndexLastPointSpawned;
                var entCoords = new EntityCoordinates(ent, info.OutputPoints[index]);
                var coords = _transformSystem.ToWorldPosition(entCoords);
                _transformSystem.SetWorldPosition(args.OtherEntity, coords);
                info.IndexLastPointSpawned = (index + 1) % info.OutputPoints.Count();
            }
        }
    }

    private bool IsEntityTransportable(EntityUid ent)
    {
        return HasComp<ItemComponent>(ent);
    }
}
