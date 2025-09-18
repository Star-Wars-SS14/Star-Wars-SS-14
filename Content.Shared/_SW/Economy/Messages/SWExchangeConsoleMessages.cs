using Content.Shared._SW.Economy.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._SW.Economy.Messages;

[NetSerializable, Serializable]
public enum SWExchangeConsoleUiKey
{
    Key,
}

[Serializable, NetSerializable]
public sealed class SWExchangeConsoleState : SWBasePayableBounderUserInterfaceState
{
    public List<ProtoId<SWCurrencyPrototype>> AvailableCurrencies = new();

    public SWExchangeConsoleState(List<ProtoId<SWCurrencyPrototype>> ac)
    {
        AvailableCurrencies = ac;
    }
}

[Serializable, NetSerializable]
public sealed class SWTryExchangeMessage : BoundUserInterfaceMessage
{
    public ProtoId<SWCurrencyPrototype> CurrencyFrom;
    public ProtoId<SWCurrencyPrototype> CurrencyTo;
    public int Amount;

    public SWTryExchangeMessage(ProtoId<SWCurrencyPrototype> from,
                                ProtoId<SWCurrencyPrototype> to,
                                int amount)
    {
        CurrencyFrom = from;
        CurrencyTo = to;
        Amount = amount;
    }

    public SWTryExchangeMessage(SWCurrencyPrototype from,
                                SWCurrencyPrototype to,
                                int amount)
    {
        CurrencyFrom = from.ID;
        CurrencyTo = to.ID;
        Amount = amount;
    }
}
