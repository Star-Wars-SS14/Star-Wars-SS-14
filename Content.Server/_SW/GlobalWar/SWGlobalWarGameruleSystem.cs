using Content.Server.GameTicking.Rules;
using Content.Server._SW.GlobalWar.Market;
using Content.Shared.GameTicking.Components;
using Content.Shared.GameTicking;

namespace Content.Server._SW.GlobalWar;

/// <summary>
/// Manages the lifecycle of the Global War game rule.
/// </summary>
public sealed class SWGlobalWarGameruleSystem : GameRuleSystem<SWGlobalWarGameruleComponent>
{
    [Dependency] private readonly SWGlobalWarPlanetSystem _planets = default!;
    [Dependency] private readonly SWGlobalWarMarketSystem _markets = default!;

    [ViewVariables]
    public Entity<SWGlobalWarGameruleComponent>? GlobalWarGamerule { get; private set; }

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWGlobalWarGameruleComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    public bool TryGetGlobalWarGamerule(out Entity<SWGlobalWarGameruleComponent> globalWarGamerule)
    {
        globalWarGamerule = default;
        if (GlobalWarGamerule is not { } current || TerminatingOrDeleted(current.Owner) ||
            !GameTicker.IsGameRuleActive(current.Owner))
            return false;

        globalWarGamerule = current;
        return true;
    }

    protected override void Added(EntityUid uid, SWGlobalWarGameruleComponent component,
        GameRuleComponent gameRule, GameRuleAddedEvent args)
    {
        base.Added(uid, component, gameRule, args);
        if (TryGetGlobalWarGamerule(out var current) && current.Owner != uid)
            ForceEndSelf(uid, gameRule);
    }

    protected override void Started(EntityUid uid, SWGlobalWarGameruleComponent component,
        GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        // Multiple pending rules can be added before any of them starts.
        if (TryGetGlobalWarGamerule(out var current) && current.Owner != uid)
        {
            ForceEndSelf(uid, gameRule);
            return;
        }

        GlobalWarGamerule = (uid, component);
        component.Generation = Guid.NewGuid();

        try
        {
            _markets.ValidateConfiguration(component);
            _planets.InitializePlanets();
            _markets.InitializeMarkets();
        }
        catch
        {
            ForceEndSelf(uid, gameRule);
            throw;
        }
    }

    protected override void Ended(EntityUid uid, SWGlobalWarGameruleComponent component,
        GameRuleComponent gameRule, GameRuleEndedEvent args)
    {
        base.Ended(uid, component, gameRule, args);
        _markets.StopMarkets(uid);
        _planets.StopPlanets((uid, component));
        if (GlobalWarGamerule?.Owner == uid)
            GlobalWarGamerule = null;
    }

    private void OnShutdown(Entity<SWGlobalWarGameruleComponent> ent, ref ComponentShutdown args)
    {
        _markets.StopMarkets(ent.Owner);
        _planets.StopPlanets(ent);
        if (GlobalWarGamerule?.Owner == ent.Owner)
            GlobalWarGamerule = null;
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        GlobalWarGamerule = null;
    }
}
