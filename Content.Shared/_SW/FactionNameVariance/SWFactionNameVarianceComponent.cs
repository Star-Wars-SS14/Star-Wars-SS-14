using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW.SWFactionNameVariance;

[RegisterComponent]
public sealed partial class SWFactionNameVarianceComponent : Component
{
    [DataField(required: true)]
    public Dictionary<string, LocId> FactionsToName = new Dictionary<string, LocId>();
}
