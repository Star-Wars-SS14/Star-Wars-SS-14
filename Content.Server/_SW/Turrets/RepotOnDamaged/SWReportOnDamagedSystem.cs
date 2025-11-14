using Content.Server.Radio.EntitySystems;
using Content.Shared.Damage;
using Robust.Shared.Timing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.Turrets.RepotOnDamaged;
public sealed class SWReportOnDamagedSystem  :EntitySystem
{
    [Dependency] private readonly RadioSystem _radio = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWReportOnDamagedComponent, DamageChangedEvent>(OnDamage);
    }

    private void OnDamage(Entity<SWReportOnDamagedComponent> ent, ref DamageChangedEvent args)
    {
        if (args.DamageIncreased &&
            _timing.CurTime > ent.Comp.NextMessageAvailable)
        {
            var message = Loc.GetString(ent.Comp.MessageId);
            _radio.SendRadioMessage(ent, message, ent.Comp.ChannelId, ent, false);
            ent.Comp.NextMessageAvailable = _timing.CurTime + ent.Comp.Delay;
        }
    }
}
