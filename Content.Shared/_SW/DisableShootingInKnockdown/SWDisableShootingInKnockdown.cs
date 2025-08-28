using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.DisableShootingInKnockdown;

public sealed partial class SWDisableShootingInKnockdown : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<KnockedDownComponent, ShotAttemptedEvent>(OnShotAttempt);
    }

    private void OnShotAttempt(Entity<KnockedDownComponent> ent, ref ShotAttemptedEvent args)
    {
        args.Cancel();
    }
}
