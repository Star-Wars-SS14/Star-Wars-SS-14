using System.Linq;
using Content.Server._SW.GlobalWar.Market;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Robust.Shared.Timing;

namespace Content.Server._SW.GlobalWar.Politics;

/// <summary>
/// Closes the campaign's ownership step after political producers. F must queue completions here and declare
/// UpdatesBefore this system. No campaign producer may mutate PlanetSystem ownership directly.
/// </summary>
public sealed class SWGlobalWarCampaignStepSystem : EntitySystem
{
    [Dependency] private readonly SWGlobalWarCampaignSystem _campaign = default!;
    [Dependency] private readonly SWGlobalWarPoliticsSystem _politics = default!;
    [Dependency] private readonly SWGlobalWarPlanetSystem _planets = default!;
    [Dependency] private readonly SWGlobalWarMarketSystem _markets = default!;
    [Dependency] private readonly SWGlobalWarLedgerSystem _ledger = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private Guid _generation;
    private GameTick? _closedTick;
    private bool _closing;
    private readonly Dictionary<(EntityUid, Guid), SWGlobalWarQueuedOwnership> _pending = new();

    /// <summary>New political commands are rejected until the complete ownership step has been published.</summary>
    public bool IsClosing => _closing;

    public override void Initialize()
    {
        base.Initialize();
        UpdatesAfter.Add(typeof(SWGlobalWarPoliticsSystem));
        SubscribeLocalEvent<SWGlobalWarCampaignStartedEvent>((ref SWGlobalWarCampaignStartedEvent ev) => Reset(ev.Generation));
        SubscribeLocalEvent<SWGlobalWarCampaignStoppedEvent>((ref SWGlobalWarCampaignStoppedEvent ev) => Reset(Guid.Empty));
    }

    /// <summary>
    /// Queues a validated server operation for this tick. Accepted means queued, not completed.
    /// Completion is an internal state writer, not an observer: other completions may still be pending.
    /// Public observers run only after every completion, ownership-state update and arbitration.
    /// A producer with delayed physical conditions supplies a final validation predicate.
    /// Rejection after tick closure leaves the producer responsible for retaining and retrying its unfinished operation.
    /// </summary>
    public bool TryQueueOwnership(EntityUid planet, SWGlobalWarPlanetLocation location, SWGlobalWarFaction owner,
        long expectedRevision, Guid generation, Guid operation, Action<bool>? completed = null, Func<bool>? validate = null)
    {
        if (_closing || _closedTick == _timing.CurTick || generation != _generation || operation == Guid.Empty ||
            !_campaign.TryGetCampaign(out var campaign) || campaign.Comp.Generation != generation ||
            campaign.Comp.Phase == SWGlobalWarCampaignPhase.Ending || !Enum.IsDefined(location) || !Enum.IsDefined(owner) ||
            !TryComp<SWGlobalWarPlanetComponent>(planet, out var data) || data.GlobalWarGamerule != campaign.Owner)
            return false;
        var queued = new SWGlobalWarQueuedOwnership(planet, location, owner, expectedRevision, generation, operation,
            completed, validate);
        if (_pending.TryGetValue((planet, operation), out var existing))
            return existing.Location == location && existing.Owner == owner && existing.ExpectedRevision == expectedRevision;
        _pending.Add((planet, operation), queued);
        return true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        CommitCampaignStep(_generation, _timing.CurTick);
    }

    /// <summary>
    /// Explicit closure used by the ordered Update and deterministic server tests. Only one closure per tick;
    /// reentrant commands cannot add operations to a partially published step.
    /// </summary>
    public bool CommitCampaignStep(Guid generation, GameTick tick)
    {
        if (_closing || generation == Guid.Empty || generation != _generation || tick != _timing.CurTick ||
            _closedTick == tick || !_campaign.TryGetCampaign(out var campaign) ||
            campaign.Comp.Phase == SWGlobalWarCampaignPhase.Ending)
            return false;
        _closing = true;
        _closedTick = tick;
        var operations = _pending.Values.ToArray();
        _pending.Clear();
        var results = new bool[operations.Length];
        try
        {
            using var ledgerNotifications = _ledger.DeferNotifications();
            if (operations.Length > 0)
                _markets.SynchronizeOwnershipBoundary(generation);
            // Validate before opening the mutation gate: predicates cannot reenter a partially applied batch.
            for (var i = 0; i < operations.Length; i++)
                results[i] = operations[i].Validate == null || operations[i].Validate!();
            if (generation != _generation)
                return false;
            _planets.BeginCampaignOwnershipBatch(generation);
            SWGlobalWarOwnershipChangedEvent[] events;
            try
            {
                for (var i = 0; i < operations.Length; i++)
                {
                    var op = operations[i];
                    results[i] = results[i] && _planets.TryChangeOwnership(op.Planet,
                        op.Location, op.Owner, op.ExpectedRevision, op.Generation, op.Operation, out _);
                }
            }
            finally
            {
                events = _planets.EndCampaignOwnershipBatch();
            }
            _markets.CommitOwnershipBatch(generation, events);
            for (var i = 0; i < operations.Length; i++)
            {
                if (generation != _generation)
                    return false;
                operations[i].Completed?.Invoke(results[i]);
            }
            _politics.CommitOwnershipState(events);
            _politics.ArbitrateCampaignStep(generation);
            // Ending has already closed all new ownership mutations before any callback observes this step.
            _planets.PublishCampaignOwnership(generation, events);
            if (generation == _generation)
                _politics.PublishCampaignStep(generation);
        }
        finally
        {
            _closing = false;
        }
        return true;
    }

    private void Reset(Guid generation)
    {
        _generation = generation;
        _closedTick = null;
        _pending.Clear();
        _planets.EndCampaignOwnershipBatch();
    }
}

internal sealed record SWGlobalWarQueuedOwnership(EntityUid Planet, SWGlobalWarPlanetLocation Location,
    SWGlobalWarFaction Owner, long ExpectedRevision, Guid Generation, Guid Operation,
    Action<bool>? Completed, Func<bool>? Validate);
