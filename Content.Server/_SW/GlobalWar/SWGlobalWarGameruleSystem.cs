using Content.Server.GameTicking.Rules;
using Content.Shared.GameTicking.Components;

namespace Content.Server._SW.GlobalWar;

/// <summary>
/// Manages the lifecycle of the Global War game rule.
/// </summary>
public sealed class SWGlobalWarGameruleSystem : GameRuleSystem<SWGlobalWarGameruleComponent>
{
    [Dependency] private readonly SWGlobalWarPlanetSystem _planets = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWGlobalWarGameruleComponent, ComponentShutdown>(OnShutdown);
    }

    protected override void Started(EntityUid uid, SWGlobalWarGameruleComponent component,
        GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        try
        {
            _planets.InitializePlanets((uid, component));
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
        _planets.StopPlanets((uid, component));
    }

    private void OnShutdown(Entity<SWGlobalWarGameruleComponent> ent, ref ComponentShutdown args)
    {
        _planets.StopPlanets(ent);
    }
}
