using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW.Species;
[Prototype("swColorLimits")]
public sealed partial class SWColorLimitsPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; } = default!;

    [DataField]
    public SWNumberIntervalSet<float> HueLimit = new SWNumberIntervalSet<float>(0, 100);

    [DataField]
    public SWNumberIntervalSet<float> SaturationLimit = new SWNumberIntervalSet<float>(0, 100);

    [DataField]
    public SWNumberIntervalSet<float> ValueLimit = new SWNumberIntervalSet<float>(0, 100);
}

