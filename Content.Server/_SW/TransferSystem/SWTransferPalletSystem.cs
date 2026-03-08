using Content.Shared.Mobs.Components;
using Robust.Shared.Physics.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.TransferSystem;

public sealed partial class SWTransferPalletSystem : EntitySystem
{
    [Dependency] private readonly EntityLookupSystem _lookupSystem = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWTransferPalletComponent, StartCollideEvent>(OnStartCollide);
    }

    private void OnStartCollide(Entity<SWTransferPalletComponent> ent, ref StartCollideEvent args)
    {
        
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<SWTransferPalletComponent>();
        while(query.MoveNext(out var uid, out var comp))
        {
            HashSet<EntityUid> res = _lookupSystem.GetEntitiesIntersecting(uid, LookupFlags.Dynamic | LookupFlags.Sundries);
            foreach (var obj in res)
            {
                if (HasComp<SWCantBeTransferredComponent>(obj))
                {
                    continue;
                }

                if (TryComp<MobStateComponent>(obj, out var mobComp))
                {
                    if (mobComp.CurrentState == Shared.Mobs.MobState.Alive)
                    {
                        continue;
                    }
                }

                var ev = new TryTransferObject(obj, comp.OwnerFaction);
                RaiseLocalEvent(ref ev);
                if (ev.Handled)
                {
                    Del(obj);
                }
            }
        }
    }
}

/// <summary>
/// Event which is raised when object is placed on pallet with SWTransferPalletComponent.
/// Other systems can subscribe and do what they need, for example check steal task or etc.
/// </summary>
[ByRefEvent]
public sealed class TryTransferObject : HandledEntityEventArgs
{
    public EntityUid PlacedUid;
    public string OwnerFaction;
    public TryTransferObject(EntityUid uid, string ownerFaction)
    {
        PlacedUid = uid;
        OwnerFaction = ownerFaction;
    }
}
