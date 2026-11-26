using Content.Shared.Humanoid;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.Server._SW.Humanoid;

/// <summary>
/// Removes HumanoidAppearanceComponent from entities that carry it with an empty
/// Species (non-humanoids, e.g. possessed vehicles), preventing the client
/// UpdateLayers Index(null) crash and the PVS-area lag.
/// Uses a periodic sweep instead of ComponentInit/MapInit subscriptions: those
/// lifecycle events on HumanoidAppearanceComponent are already owned by
/// SharedHumanoidAppearanceSystem, and a second directed subscription throws
/// Duplicate Subscriptions at startup.
/// </summary>
public sealed class SWHumanoidAppearanceSanitizeSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;

    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(1);
    private TimeSpan _nextSweep;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextSweep)
            return;

        _nextSweep = _timing.CurTime + SweepInterval;

        var query = EntityQueryEnumerator<HumanoidAppearanceComponent>();
        while (query.MoveNext(out var uid, out var appearance))
        {
            // Real humanoids always have a species; an empty one means a bogus
            // component on a non-humanoid entity.
            if (!string.IsNullOrEmpty(appearance.Species))
                continue;

            RemCompDeferred<HumanoidAppearanceComponent>(uid);
        }
    }
}