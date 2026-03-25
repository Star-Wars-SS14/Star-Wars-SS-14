using Content.Shared.Temperature;
using Content.Shared.Temperature.Components;
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
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<TemperatureComponent, SWTriggerOnHighTemperatureComponent>();
        while (query.MoveNext(out var uid, out var tempComp, out var trigComp))
        {
            if (tempComp.CurrentTemperature > trigComp.TriggerThreshold)
            {
                _triggerSystem.Trigger(uid);
            }
        }
    }

    private void OnTempChange(Entity<SWTriggerOnHighTemperatureComponent> ent, ref OnTemperatureChangeEvent args)
    {
        if (args.CurrentTemperature > ent.Comp.TriggerThreshold)
        {
            _triggerSystem.Trigger(ent.Owner);
        }
    }
}
