using Content.Shared._SW.GlobalWar.Combat;
using Content.Shared._SW.GlobalWar.Politics;
using Content.Shared.GameTicking;
using Content.Shared.GameTicking.Components;
using Robust.Shared.Timing;

namespace Content.Server._SW.GlobalWar.Politics;

/// <summary>Uses the real simulation clock; map sleeping never stops political time.</summary>
public sealed class SWGlobalWarCampaignSystem : EntitySystem
{
    [Dependency] private readonly SWGlobalWarGameruleSystem _rules = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWGlobalWarCampaignComponent, GameRuleStartedEvent>(OnStarted,
            after: [typeof(SWGlobalWarGameruleSystem)]);
        SubscribeLocalEvent<SWGlobalWarCampaignComponent, GameRuleEndedEvent>(OnEnded);
        SubscribeLocalEvent<SWGlobalWarCampaignComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRestart);
    }

    /// <summary>Resolves only the political component belonging to the current active GlobalWar generation.</summary>
    public bool IsShipClassDisabled(SWGlobalWarShipClass shipClass)
    {
        return TryGetCampaign(out var campaign) && campaign.Comp.DisabledShipClasses.Contains(shipClass);
    }

    public bool TryGetCampaign(out Entity<SWGlobalWarCampaignComponent> campaign)
    {
        campaign = default;
        if (!_rules.TryGetGlobalWarGamerule(out var rule) ||
            !TryComp<SWGlobalWarCampaignComponent>(rule.Owner, out var state) ||
            state.Generation == Guid.Empty || state.Generation != rule.Comp.Generation ||
            state.Phase == SWGlobalWarCampaignPhase.Stopped)
            return false;
        campaign = (rule.Owner, state);
        return true;
    }

    /// <summary>Elapsed simulation time, frozen after completion; auto_pause_empty stops CurTime in the game loop.</summary>
    public TimeSpan Elapsed(Entity<SWGlobalWarCampaignComponent> campaign)
    {
        if (campaign.Comp.StoppedElapsed is { } stopped)
            return stopped;
        return TimeSpan.FromTicks(Math.Max(0, (_timing.CurTime - campaign.Comp.StartedAt).Ticks));
    }

    /// <summary>Applies a forward lifecycle transition after the political operation has been validated.</summary>
    public bool TryAdvancePhase(Guid generation, SWGlobalWarCampaignPhase next)
    {
        if (!TryGetCampaign(out var campaign) || campaign.Comp.Generation != generation)
            return false;
        var valid = (campaign.Comp.Phase, next) is
            (SWGlobalWarCampaignPhase.Running, SWGlobalWarCampaignPhase.Capitulating) or
            (SWGlobalWarCampaignPhase.Running, SWGlobalWarCampaignPhase.Ending) or
            (SWGlobalWarCampaignPhase.Capitulating, SWGlobalWarCampaignPhase.Ending);
        if (!valid)
            return false;
        if (next == SWGlobalWarCampaignPhase.Ending)
            campaign.Comp.StoppedElapsed = Elapsed(campaign);
        campaign.Comp.Phase = next;
        return true;
    }

    private void OnStarted(Entity<SWGlobalWarCampaignComponent> ent, ref GameRuleStartedEvent args)
    {
        if (!_rules.TryGetGlobalWarGamerule(out var rule) || rule.Owner != ent.Owner ||
            rule.Comp.Generation == ent.Comp.Generation)
            return;
        ent.Comp.Generation = rule.Comp.Generation;
        // Rule activation precedes GameTicker assigning RoundStartTimeSpan in the same synchronous start.
        // Use its CurTime origin so initialization never reads a previous round's duration.
        ent.Comp.StartedAt = _timing.CurTime;
        ent.Comp.StoppedElapsed = null;
        ent.Comp.Phase = SWGlobalWarCampaignPhase.Running;
        var started = new SWGlobalWarCampaignStartedEvent(ent.Owner, ent.Comp.Generation);
        RaiseLocalEvent(ref started);
    }

    private void OnEnded(Entity<SWGlobalWarCampaignComponent> ent, ref GameRuleEndedEvent args)
    {
        Stop(ent);
    }

    private void OnShutdown(Entity<SWGlobalWarCampaignComponent> ent, ref ComponentShutdown args)
    {
        Stop(ent);
    }

    private void OnRestart(RoundRestartCleanupEvent args)
    {
        var query = AllEntityQuery<SWGlobalWarCampaignComponent>();
        while (query.MoveNext(out var uid, out var campaign))
            Stop((uid, campaign));
    }

    private void Stop(Entity<SWGlobalWarCampaignComponent> ent)
    {
        if (ent.Comp.Generation == Guid.Empty || ent.Comp.Phase == SWGlobalWarCampaignPhase.Stopped)
            return;
        ent.Comp.StoppedElapsed = Elapsed(ent);
        ent.Comp.Phase = SWGlobalWarCampaignPhase.Stopped;
        var stopped = new SWGlobalWarCampaignStoppedEvent(ent.Owner, ent.Comp.Generation);
        RaiseLocalEvent(ref stopped);
    }
}
