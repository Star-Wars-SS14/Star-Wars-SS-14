using Content.Shared._SW.Economy.Prototypes;
using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.TaskSystem.SWAddMoneyOnCompleted;

[RegisterComponent]
public sealed partial class SWAddMoneyOnCompletedComponent : Component
{
    [DataField]
    public Dictionary<ProtoId<SWCurrencyPrototype>, float> Amount = new Dictionary<ProtoId<SWCurrencyPrototype>, float>();

    [DataField]
    public LocId DescriptionId = "sw-add-money-on-completed";

    public bool Added = false;
}
