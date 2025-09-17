using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW.Economy;

[NetSerializable, Serializable]
public enum SWFinanceConsoleUIKey
{
    Key,
}

[Serializable, NetSerializable]
public sealed class SWFinanceConsoleBounderUserInterfaceState : SWBasePayableBounderUserInterfaceState
{
    public List<SWBill> Bills = new ();
}

[Serializable, NetSerializable]
public sealed class SWTryPayBillMessage : BoundUserInterfaceMessage
{
    public SWBill Bill;

    public SWTryPayBillMessage(SWBill num)
    {
        Bill = num;
    }
}

