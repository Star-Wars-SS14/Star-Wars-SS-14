using Content.Shared._SW.Economy.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW.Economy.Messages;

[NetSerializable, Serializable]
public enum SWBasePayableConsoleUIKey
{
    Key,
}

[Serializable, NetSerializable]
public class SWBasePayableBounderUserInterfaceState : BoundUserInterfaceState
{
    public Dictionary<ProtoId<SWCurrencyPrototype>, float> CurrentAmount = new();
}

[Serializable, NetSerializable]
public sealed class SWTryPayoutMessage : BoundUserInterfaceMessage
{
    public Dictionary<ProtoId<SWCurrencyPrototype>, float> Requests = new();
    public SWTryPayoutMessage(Dictionary<ProtoId<SWCurrencyPrototype>, float> dict)
    {
        Requests = dict;
    }
}


[Serializable, NetSerializable]
public sealed class SWTryAddMoneyMessage : BoundUserInterfaceMessage
{

}

[Serializable, NetSerializable]
public enum SWConsoleAction
{
    Payout,
    Purchase,
}

/// <summary>
/// Sent to the client when an action went through, so its window closes only then.
/// A refusal sends no such message: the window stays open and the server shows the reason in a popup.
/// </summary>
[Serializable, NetSerializable]
public sealed class SWConsoleActionSucceededMessage : BoundUserInterfaceMessage
{
    public SWConsoleAction Action;

    public SWConsoleActionSucceededMessage(SWConsoleAction action)
    {
        Action = action;
    }
}
