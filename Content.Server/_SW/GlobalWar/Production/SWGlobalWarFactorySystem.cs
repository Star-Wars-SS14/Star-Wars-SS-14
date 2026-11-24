using Content.Server._SW.GlobalWar.Market;

namespace Content.Server._SW.GlobalWar.Production;

/// <summary>Ticks powered physical work after the market's authoritative campaign step.</summary>
public sealed class SWGlobalWarFactorySystem : EntitySystem
{
    [Dependency] private readonly SWGlobalWarMarketSystem _markets = default!;

    public override void Initialize()
    {
        base.Initialize();
        UpdatesAfter.Add(typeof(SWGlobalWarMarketSystem));
    }

    public override void Update(float frameTime)
    {
        _markets.UpdatePhysicalFactories(frameTime);
    }
}
