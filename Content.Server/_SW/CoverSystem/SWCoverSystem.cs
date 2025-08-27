using Content.Server.Projectiles;
using Content.Shared.FixedPoint;
using Content.Shared.Projectiles;
using Robust.Shared.Physics.Events;
using Robust.Shared.Random;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.CoverSystem;
public sealed partial class SWCoverSystem : EntitySystem
{
    [Dependency] IRobustRandom _random = default!;
    [Dependency] SharedTransformSystem _transformSystem = default!;
    //I dont like it. Theres must be a better solution

    public static int ProjectilesListTooBigSize = 20;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWCoverComponent, PreventCollideEvent>(OnCollide);
    }

    private void OnCollide(Entity<SWCoverComponent> ent, ref PreventCollideEvent args)
    {
        if (ent.Comp.HandledProjectiles.ContainsKey(args.OtherEntity))
        {
            args.Cancelled = ent.Comp.HandledProjectiles[args.OtherEntity];
        }

        ISawmill _s = Logger.GetSawmill("cover");
        _s.Fatal("1");
        if (!TryComp<ProjectileComponent>(args.OtherEntity, out var comp) || comp.Shooter == null)
        {
            return;
        }
        _s.Fatal("2");
        var pos_shooter = _transformSystem.GetWorldPosition(comp.Shooter.Value);
        var pos_our = _transformSystem.GetWorldPosition(args.OurEntity);
        var distance = (pos_shooter - pos_our).Length();

        float chanceToPassCollision = 1.0f - (distance / ent.Comp.Step) * ent.Comp.ChanceDecreasePerStep;

        chanceToPassCollision = chanceToPassCollision > 0f ? chanceToPassCollision : 0f;

        _s.Fatal($"distance: {distance}, chance{chanceToPassCollision}");
        if (distance <= ent.Comp.DistanceGuaranteedPass || _random.Prob(chanceToPassCollision))
        {
            args.Cancelled = true;
            ent.Comp.HandledProjectiles[args.OtherEntity] = true;
            _s.Fatal("passed");
        }
        else
        {
            ent.Comp.HandledProjectiles[args.OtherEntity] = false;
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<SWCoverComponent>();
        while (query.MoveNext(out var _, out var comp))
        {
            if (comp.HandledProjectiles.Count() >= ProjectilesListTooBigSize)
            {
                comp.HandledProjectiles = ClearProjectils(comp.HandledProjectiles);
            }
        }
    }

    private Dictionary<EntityUid, bool> ClearProjectils(Dictionary<EntityUid, bool> handledProjectiles)
    {
        Dictionary<EntityUid, bool> res = new Dictionary<EntityUid, bool>();
        foreach (var (ent, cancelled) in handledProjectiles)
        {
            if (!Deleted(ent))
            {
                res[ent] = cancelled;
            }
        }
        return res;
    }
}
