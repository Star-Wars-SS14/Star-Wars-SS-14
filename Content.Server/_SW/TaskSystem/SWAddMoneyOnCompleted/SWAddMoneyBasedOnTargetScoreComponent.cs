using Content.Shared._SW.Economy.Prototypes;
using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.TaskSystem.SWAddMoneyOnCompleted;

[RegisterComponent]
public sealed partial class SWAddMoneyBasedOnTargetScoreComponent : Component
{
    [DataField("amount", required: true)]
    public Dictionary<ProtoId<SWCurrencyPrototype>, float> MoneyPerScore = new Dictionary<ProtoId<SWCurrencyPrototype>, float>();

    public bool Added = false;
}
