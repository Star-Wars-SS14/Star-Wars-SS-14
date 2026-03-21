using Content.Server._SW.Gamerules.RadioactiveStorm;
using Content.Shared.Trigger;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.CauseRadioactiveStormOnTrigger;
public sealed class SWCauseRadioactiveStormOnTriggerSystem : EntitySystem
{
    [Dependency] private readonly SWRadioactiveStormGameruleSystem _sWRadioactiveStorm = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWCauseRadioactiveStormOnTriggerComponent, TriggerEvent>(OnTrigger);
    }

    private void OnTrigger(Entity<SWCauseRadioactiveStormOnTriggerComponent> ent, ref TriggerEvent args)
    {
        _sWRadioactiveStorm.StartStorm();
    }
}
