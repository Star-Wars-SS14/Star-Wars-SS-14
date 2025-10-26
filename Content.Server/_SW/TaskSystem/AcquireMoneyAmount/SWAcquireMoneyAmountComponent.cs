using Content.Shared._SW.Economy.BankSystem;
using Content.Shared._SW.Economy.Prototypes;
using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.TaskSystem.AcquireMoneyAmount;

[RegisterComponent]
public sealed partial class SWAcquireMoneyAmountComponent : Component
{
    [DataField]
    public Dictionary<ProtoId<SWCurrencyPrototype>, float> Amount = new Dictionary<ProtoId<SWCurrencyPrototype>, float>();

    public Entity<SWBankAccountContainerComponent>? Account;
}
