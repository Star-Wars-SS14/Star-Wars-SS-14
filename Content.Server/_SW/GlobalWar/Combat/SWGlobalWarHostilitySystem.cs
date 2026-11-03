using Content.Server._SW.Droids;
using Content.Server._SW.GlobalWar.Identity;
using Content.Server._SW.GlobalWar.Politics;
using Content.Server._SW.Targeting;
using Content.Server.Administration.Managers;
using Content.Server.Popups;
using Content.Shared._SW.Droids;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Content.Shared.Administration;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Trigger;
using Content.Shared.Explosion.Components;
using Content.Shared.Explosion;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._SW.GlobalWar.Combat;

/// <summary>Rechecks campaign politics when firing and when attributed damage actually lands.</summary>
public sealed class SWGlobalWarHostilitySystem : EntitySystem
{
    [Dependency] private readonly SWGlobalWarCampaignSystem _campaign = default!;
    [Dependency] private readonly SWGlobalWarPlanetSystem _planets = default!;
    [Dependency] private readonly SWGlobalWarPoliticsSystem _politics = default!;
    [Dependency] private readonly SWGlobalWarIdentitySystem _identity = default!;
    [Dependency] private readonly SharedMapSystem _maps = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IAdminManager _admins = default!;

    /// <summary>A blocked trigger is pulled many times a second; say why once in a while.</summary>
    private static readonly TimeSpan PeaceNoticeInterval = TimeSpan.FromSeconds(3);

    private readonly HashSet<EntityUid> _knownActors = new();
    private readonly Dictionary<EntityUid, EntityUid> _explosiveUsers = new();
    private readonly Dictionary<EntityUid, TimeSpan> _peaceNotices = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<GunComponent, ShotAttemptedEvent>(OnShot);
        SubscribeLocalEvent<DamageableComponent, BeforeDamageChangedEvent>(OnDamage);
        SubscribeLocalEvent<SWAutoTargetingGunComponent, SWAutoTargetingGunAttemptEvent>(OnAutoGun);
        SubscribeLocalEvent<ExplosiveComponent, AttemptTriggerEvent>(OnExplosive);
        SubscribeLocalEvent<SWExplosionAttemptEvent>(OnExplosion);
        SubscribeLocalEvent<SWDroidCommandableComponent, SWDroidTargetAllegianceEvent>(OnDroidTarget);
        SubscribeLocalEvent<ExplosiveComponent, ActiveTimerTriggerEvent>(OnArmed);
        SubscribeLocalEvent<SWGlobalWarCampaignStartedEvent>((ref SWGlobalWarCampaignStartedEvent _) =>
        {
            _knownActors.Clear();
            _explosiveUsers.Clear();
            _peaceNotices.Clear();
        });
    }

    private bool Allowed(EntityUid actor, EntityUid target)
    {
        if (!_campaign.TryGetCampaign(out var campaign) || !_planets.TryGetPhysicalLocation(target, out var planet, out _))
            return true;
        // Admin debugging: an active admin's attack is never cancelled, peace included.
        if (TryComp<ActorComponent>(actor, out var player) && _admins.HasAdminFlag(player.PlayerSession, AdminFlags.Admin))
            return true;
        if (_identity.TryGetIdentity(actor, out _, out _))
        {
            _knownActors.Add(actor);
            return _politics.CanConductHostility(actor, planet, campaign.Comp.Generation);
        }
        if (HasComp<SWDroidCommandableComponent>(actor) || HasComp<SWAutoTargetingGunComponent>(actor))
        {
            _knownActors.Add(actor);
            return _politics.CanConductNpcHostility(planet, campaign.Comp.Generation);
        }
        return !_knownActors.Contains(actor);
    }

    private void OnShot(Entity<GunComponent> ent, ref ShotAttemptedEvent args)
    {
        if (Allowed(args.User, args.User))
            return;
        args.Cancel();
        NotifyPeace(args.User);
    }

    /// <summary>Peace closes combat by design; without a word the shooter takes it for a broken gun.</summary>
    private void NotifyPeace(EntityUid shooter)
    {
        if (!_politics.TryGetPolitics(out var politics) || politics.Comp.Relations != SWGlobalWarRelations.Peace ||
            !_identity.TryGetIdentity(shooter, out _, out _) ||
            _peaceNotices.TryGetValue(shooter, out var next) && _timing.CurTime < next)
            return;
        _peaceNotices[shooter] = _timing.CurTime + PeaceNoticeInterval;
        _popup.PopupEntity(Loc.GetString("sw-global-war-hostility-peace"), shooter, shooter);
    }

    private void OnDamage(Entity<DamageableComponent> ent, ref BeforeDamageChangedEvent args)
    {
        if (args.Origin is { } actor && args.Damage.GetTotal() > 0 && !Allowed(actor, ent.Owner))
            args.Cancelled = true;
    }

    private void OnAutoGun(EntityUid uid, SWAutoTargetingGunComponent component, SWAutoTargetingGunAttemptEvent args)
    {
        if (!Allowed(uid, args.Target)) args.Cancel();
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

    private void OnExplosive(Entity<ExplosiveComponent> ent, ref AttemptTriggerEvent args)
    {
        if (ResolveInitiator(ent.Owner, args.User) is { } actor && !Allowed(actor, ent.Owner))
            args.Cancelled = true;
    }

    private void OnExplosion(ref SWExplosionAttemptEvent args)
    {
        if (TryComp<SWGlobalWarProjectileRangeComponent>(args.Cause, out var range) && range.Remaining <= 0)
            args.Cancelled = true;
        args.User = ResolveInitiator(args.Cause, args.User);
        args.SeparateInitiators = _campaign.TryGetCampaign(out _) &&
            _planets.TryGetPhysicalLocation(_maps.GetMapOrInvalid(args.Epicenter.MapId), out _, out _);
        if (args.User is { } actor && !Allowed(actor, _maps.GetMapOrInvalid(args.Epicenter.MapId)))
            args.Cancelled = true;
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
            _knownActors.Add(actor);
            _explosiveUsers[ent.Owner] = actor;
        }
    }
}
