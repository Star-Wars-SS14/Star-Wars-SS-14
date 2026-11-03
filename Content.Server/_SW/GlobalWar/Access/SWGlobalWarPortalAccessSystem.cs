using Content.Shared._SW.GlobalWar.Access;
using Content.Shared.Movement.Pulling.Components;

namespace Content.Server._SW.GlobalWar.Access;

/// <summary>Applies the common policy to real portal transfers, including gateway-created portals.</summary>
public sealed class SWGlobalWarPortalAccessSystem : EntitySystem
{
    [Dependency] private readonly SWGlobalWarGameruleSystem _rules = default!;
    [Dependency] private readonly SWGlobalWarPlanetSystem _planets = default!;
    [Dependency] private readonly SWGlobalWarAccessPolicySystem _policy = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWGlobalWarPortalAttemptEvent>(OnAttempt);
    }

    private void OnAttempt(ref SWGlobalWarPortalAttemptEvent args)
    {
        if (args.Cancelled || !_rules.TryGetGlobalWarGamerule(out var rule) ||
            !TryComp<TransformComponent>(args.Subject, out var source) ||
            !TryComp<TransformComponent>(args.Target.EntityId, out var target) || source.MapUid == target.MapUid)
            return;
        if (!HasComp<SWGlobalWarPlanetMapComponent>(source.MapUid) && !HasComp<SWGlobalWarPlanetMapComponent>(target.MapUid))
            return;

        // Group freight needs the atomic logistics operation; a portal must not sever a patient's pull chain.
        if (TryComp<PullableComponent>(args.Subject, out var pulled) && pulled.BeingPulled ||
            TryComp<PullerComponent>(args.Subject, out var pulling) && pulling.Pulling != null ||
            !_planets.TryGetPhysicalLocation(args.Target.EntityId, out var planet, out var location))
        {
            args.Cancelled = true;
            return;
        }
        var decision = _policy.EvaluateTransition(args.Subject, planet, location, SWGlobalWarTransitionKind.Portal,
            rule.Comp.Generation);
        args.Cancelled = !decision.Allowed || !_planets.TryEnsureLocation(planet, location, out _, out _);
    }
}
