using Content.Server._SW.GlobalWar.Politics;
using Content.Server._SW.Targeting;
using Content.Shared._SW.Droids;
using Content.Shared.PowerCell.Components;
using Content.Shared.Projectiles;
using Robust.Shared.Timing;

namespace Content.Server._SW.GlobalWar.Combat;

/// <summary>Initializes autonomous campaign droids independently of production or command consoles.</summary>
public sealed class SWGlobalWarDroidSystem : EntitySystem
{
    [Dependency] private readonly SWGlobalWarCampaignSystem _campaign = default!;
    [Dependency] private readonly SWGlobalWarPlanetSystem _planets = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    private TimeSpan _nextUpdate;

    public override void Initialize()
    {
        SubscribeLocalEvent<SWDroidCommandableComponent, EntityTerminatingEvent>(OnDroidTerminating);
    }

    public override void Update(float frameTime)
    {
        if (!_campaign.TryGetCampaign(out _) || _timing.CurTime < _nextUpdate) return;
        _nextUpdate = _timing.CurTime + TimeSpan.FromSeconds(1);
        PrepareDroids();
    }

    private void OnDroidTerminating(Entity<SWDroidCommandableComponent> ent, ref EntityTerminatingEvent args)
    {
        if (!_campaign.TryGetCampaign(out _))
            return;
        var query = EntityQueryEnumerator<ProjectileComponent>();
        while (query.MoveNext(out var bolt, out var projectile))
        {
            if (projectile.Shooter == ent.Owner)
                QueueDel(bolt);
        }
    }

    private void PrepareDroids()
    {
        var query = EntityQueryEnumerator<SWDroidCommandableComponent>();
        while (query.MoveNext(out var droid, out var settings))
        {
            if (string.IsNullOrEmpty(settings.Faction) || HasComp<SWTargetingConfigComponent>(droid) ||
                !_planets.TryGetPhysicalLocation(droid, out _, out _))
                continue;
            AddComp<SWTargetingConfigComponent>(droid).TargetHumanoidsWithoutId = true;
            RemComp<PowerCellDrawComponent>(droid);
        }
    }

}
