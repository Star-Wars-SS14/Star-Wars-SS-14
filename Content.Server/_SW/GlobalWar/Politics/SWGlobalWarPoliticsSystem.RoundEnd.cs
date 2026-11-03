using Content.Server.GameTicking;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Content.Shared.GameTicking;

namespace Content.Server._SW.GlobalWar.Politics;

public sealed partial class SWGlobalWarPoliticsSystem
{
    private readonly SWGlobalWarWarTimeline _warTimeline = new();

    private void InitializeRoundEnd()
    {
        SubscribeLocalEvent<SWGlobalWarOwnershipChangedEvent>(OnOwnershipTick);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => _warTimeline.Clear());
        SubscribeLocalEvent<RoundEndTextAppendEvent>(OnRoundEndText);
        SubscribeLocalEvent<RoundEndMessageEvent>(OnRoundEndMessage);
    }

    /// <summary>Counts worlds a side holds whole. A world split between owners or still neutral belongs to neither.</summary>
    public (int Republic, int Empire, int Other, int Changes) CountWorlds(Entity<SWGlobalWarPoliticsComponent> politics)
    {
        var republic = 0;
        var empire = 0;
        var other = 0;
        var changes = 0;
        foreach (var uid in Comp<SWGlobalWarGameruleComponent>(politics.Owner).PlanetEntities.Values)
        {
            if (!TryComp<SWGlobalWarPlanetComponent>(uid, out var planet))
                continue;
            changes += (int) planet.OwnershipRevision;
            if (planet.OrbitOwner == SWGlobalWarFaction.Republic && planet.SurfaceOwner == SWGlobalWarFaction.Republic)
                republic++;
            else if (planet.OrbitOwner == SWGlobalWarFaction.Empire && planet.SurfaceOwner == SWGlobalWarFaction.Empire)
                empire++;
            else
                other++;
        }
        return (republic, empire, other, changes);
    }

    private void OnOwnershipTick(ref SWGlobalWarOwnershipChangedEvent args)
    {
        if (TryGetPolitics(out var politics) && _campaign.TryGetCampaign(out var campaign))
            _warTimeline.Record(politics.Comp.Generation, _campaign.Elapsed(campaign), args.Owner);
    }

    private void OnRoundEndText(RoundEndTextAppendEvent args)
    {
        if (!TryGetPolitics(out var politics))
            return;
        var (republic, empire, other, _) = CountWorlds(politics);
        args.AddLine(Loc.GetString("sw-global-war-round-end-worlds",
            ("republic", republic), ("empire", empire), ("other", other)));
    }

    /// <summary>The ordinary summary has just gone out, so this window opens on top of it.</summary>
    private void OnRoundEndMessage(RoundEndMessageEvent args)
    {
        if (!TryGetPolitics(out var politics) || !_campaign.TryGetCampaign(out var campaign))
            return;
        var (republic, empire, other, changes) = CountWorlds(politics);
        var duration = _campaign.Elapsed(campaign);
        var timeline = _warTimeline.Snapshot(politics.Comp.Generation);
        foreach (var session in _players.Sessions)
        {
            var side = _identity.TryGetMembership(session.UserId, out var member) ? member : SWGlobalWarFaction.Neutral;
            RaiseNetworkEvent(new SWGlobalWarRoundEndEvent(politics.Comp.Outcome, duration, side,
                republic, empire, other, changes, timeline, politics.Comp.LimitScore?.Republic,
                politics.Comp.LimitScore?.Empire), session);
        }
        Log.Info($"Campaign finished: outcome {politics.Comp.Outcome}, {duration.TotalMinutes:F0} min, " +
                 $"worlds R{republic} E{empire} other {other}, {changes} ownership changes.");
    }
}
