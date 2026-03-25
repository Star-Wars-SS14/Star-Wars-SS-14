using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.TriggerOnHighTemperature;

[RegisterComponent]
public sealed partial class SWTriggerOnHighTemperatureComponent : Component
{
    [DataField("threshold")]
    public float TriggerThreshold = 260.0f;
}
