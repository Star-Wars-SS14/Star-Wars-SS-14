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
    public SWNumberIntervalSet HueLimit = new SWNumberIntervalSet(0, 1);

    [DataField]
    public SWNumberIntervalSet SaturationLimit = new SWNumberIntervalSet(0, 1);

    [DataField]
    public SWNumberIntervalSet ValueLimit = new SWNumberIntervalSet(0, 1);
}

