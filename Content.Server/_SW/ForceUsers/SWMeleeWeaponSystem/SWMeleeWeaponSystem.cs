using Content.Server.Weapons.Melee;
using Content.Shared._SW;
using Content.Shared.Popups;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.Force.Systems;

public sealed partial class SWMeleeWeaponSystem : SWSharedMeleeWeaponSystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWForceUserBlockComponent, HitByLightningEvent>(OnLightningHit);
    }

    private void OnLightningHit(Entity<SWForceUserBlockComponent> ent, ref HitByLightningEvent args)
    {
        if (ent.Comp.IsBlocking)
        {
            args.Cancel();
            PopupSystem.PopupPredicted("Удар отражен!", ent.Owner, null, PopupType.Medium);
        }
    }
}
