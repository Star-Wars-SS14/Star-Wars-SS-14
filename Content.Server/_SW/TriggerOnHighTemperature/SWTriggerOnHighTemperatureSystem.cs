using Content.Shared.Temperature;
using Content.Shared.Trigger.Systems;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.TriggerOnHighTemperature;
public sealed class SWTriggerOnHighTemperatureSystem : EntitySystem
{
    [Dependency] private readonly TriggerSystem _triggerSystem = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWTriggerOnHighTemperatureComponent, OnTemperatureChangeEvent>(OnTempChange);
    }

    private void OnTempChange(Entity<SWTriggerOnHighTemperatureComponent> ent, ref OnTemperatureChangeEvent args)
    {
        if (args.CurrentTemperature > ent.Comp.TriggerThreshold)
        {
            _triggerSystem.Trigger(ent.Owner);
        }
    }
}
