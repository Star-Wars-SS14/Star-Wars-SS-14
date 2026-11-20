namespace Content.Server._SW.GlobalWar.Market;

/// <summary>An automatically discovered physical endpoint of the local planetary market.</summary>
[RegisterComponent]
public sealed partial class SWGlobalWarMarketPalletComponent : Component
{
    [DataField] public bool Deposit;
}
