namespace Content.Server._SW.GlobalWar.Market;

/// <summary>Market state on a planet controller in nullspace, independent of its paused game maps.</summary>
[RegisterComponent, Access(typeof(SWGlobalWarMarketSystem))]
public sealed partial class SWGlobalWarMarketComponent : Component
{
    [ViewVariables]
    public EntityUid GlobalWarGamerule;

    [ViewVariables]
    public SWGlobalWarMarketState State = default!;
}
