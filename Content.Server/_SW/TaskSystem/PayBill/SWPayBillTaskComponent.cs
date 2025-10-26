using Content.Shared._SW.Economy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.TaskSystem.PayBill;

[RegisterComponent]
public sealed partial class SWPayBillTaskComponent : Component
{
    [DataField(required: true)]
    public SWBill Bill;

    public EntityUid? FactionAccount;
}
