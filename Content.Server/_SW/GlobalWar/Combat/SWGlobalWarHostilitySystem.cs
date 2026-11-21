using Content.Server._SW.Droids;
using Content.Server._SW.GlobalWar.Identity;
using Content.Server._SW.GlobalWar.Politics;
using Content.Shared._SW.Droids;
using Content.Shared._SW.GlobalWar;
using Content.Shared.Trigger;
using Content.Shared.Explosion.Components;
using Content.Shared.Explosion;
using Content.Shared.Projectiles;

namespace Content.Server._SW.GlobalWar.Combat;

/// <summary>Tracks explosive attribution and droid allegiance without restricting combat by politics or role.</summary>
public sealed class SWGlobalWarHostilitySystem : EntitySystem
{
    [Dependency] private readonly SWGlobalWarCampaignSystem _campaign = default!;
    [Dependency] private readonly SWGlobalWarPlanetSystem _planets = default!;
    [Dependency] private readonly SWGlobalWarIdentitySystem _identity = default!;
    [Dependency] private readonly SharedMapSystem _maps = default!;

    private readonly Dictionary<EntityUid, EntityUid> _explosiveUsers = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<SWExplosionAttemptEvent>(OnExplosion);
        SubscribeLocalEvent<SWDroidCommandableComponent, SWDroidTargetAllegianceEvent>(OnDroidTarget);
        SubscribeLocalEvent<ExplosiveComponent, ActiveTimerTriggerEvent>(OnArmed);
        SubscribeLocalEvent<SWGlobalWarCampaignStartedEvent>((ref SWGlobalWarCampaignStartedEvent _) =>
        {
            _explosiveUsers.Clear();
        });
    }

    private void OnDroidTarget(Entity<SWDroidCommandableComponent> ent, ref SWDroidTargetAllegianceEvent args)
    {
        if (!_campaign.TryGetCampaign(out _) || DroidSide(ent.Comp) is not { } side)
            return;
        if (_identity.TryGetIdentity(args.Target, out _, out var faction))
            args.Protected = faction == side;
        else if (TryComp<SWDroidCommandableComponent>(args.Target, out var other))
            args.Protected = DroidSide(other) == side;
    }

    private static SWGlobalWarFaction? DroidSide(SWDroidCommandableComponent droid)
    {
        return droid.Faction switch
        {
            "RepublicFaction" => SWGlobalWarFaction.Republic,
            "EmpireFaction" => SWGlobalWarFaction.Empire,
            _ => null,
        };
    }

    private void OnExplosion(ref SWExplosionAttemptEvent args)
    {
        if (TryComp<SWGlobalWarProjectileRangeComponent>(args.Cause, out var range) && range.Remaining <= 0)
            args.Cancelled = true;
        args.User = ResolveInitiator(args.Cause, args.User);
        args.SeparateInitiators = _campaign.TryGetCampaign(out _) &&
            _planets.TryGetPhysicalLocation(_maps.GetMapOrInvalid(args.Epicenter.MapId), out _, out _);
    }

    private EntityUid? ResolveInitiator(EntityUid? source, EntityUid? user)
    {
        // Collision triggers supply the struck entity as User. That entity is never the shooter.
        if (TryComp<ProjectileComponent>(source, out var projectile))
            return projectile.Shooter;
        if (source is { } cause && _explosiveUsers.TryGetValue(cause, out var initiator))
            return initiator;
        return user;
    }

    private void OnArmed(Entity<ExplosiveComponent> ent, ref ActiveTimerTriggerEvent args)
    {
        if (_campaign.TryGetCampaign(out _) && args.User is { } actor && _identity.TryGetIdentity(actor, out _, out _))
        {
            _explosiveUsers[ent.Owner] = actor;
        }
    }
}
