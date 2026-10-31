using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW.Economy.Messages;

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
    public int BillId;

    public SWTryPayBillMessage(int billId)
    {
        BillId = billId;
    }
}

