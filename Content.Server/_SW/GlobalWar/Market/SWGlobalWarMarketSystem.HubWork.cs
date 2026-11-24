using System.Linq;
using Content.Server._SW.GlobalWar.Politics;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Content.Shared.DoAfter;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._SW.GlobalWar.Market;

public sealed partial class SWGlobalWarMarketSystem
{
    public static readonly TimeSpan HubUpgradeDuration = TimeSpan.FromSeconds(120);
    private readonly Dictionary<(NetUserId, Guid), SWGlobalWarHubWork> _hubWork = new();
    private bool _hubMutating;
    private readonly List<SWGlobalWarHubJournalEntry> _hubJournal = new();
    public IReadOnlyList<SWGlobalWarHubJournalEntry> HubJournal => _hubJournal;

    /// <summary>Prices depend on the absolute next capacity, so captures and rebuilt devices do not reset progression.</summary>
    public static bool TryGetHubUpgrade(SWGlobalWarMarketState market, out int capacity, out long price)
    {
        capacity = market.HubCapacity;
        price = 0;
        if (market.Profile == null || capacity >= market.Profile.MaximumHubCapacity)
            return false;
        (capacity, price) = capacity switch
        {
            25 => (50, 300000L),
            50 => (75, 600000L),
            75 => (100, 1200000L),
            150 => (200, 600000L),
            200 => (250, 1200000L),
            _ => (capacity, 0L),
        };
        return price > 0 && capacity <= market.Profile.MaximumHubCapacity;
    }

    /// <summary>Starts one physical attempt after catching the existing market up under its previous capacity.</summary>
    public bool TryStartHubUpgrade(ICommonSession session, EntityUid hub, SWGlobalWarHubCommand command,
        out SWGlobalWarHubResult? result, out SWGlobalWarHubFailure failure)
    {
        result = null;
        failure = SWGlobalWarHubFailure.Denied;
        if (command == null || command.Request == Guid.Empty || !CanMutateCampaign(command.Generation) ||
            !_assignments.TryGetActor(session, command.Generation, command.AssignmentRevision, out var actor) ||
            !HasHubAuthority(actor, hub, command))
            return false;
        var key = (session.UserId, command.Request);
        if (_hubWork.TryGetValue(key, out var old))
        {
            if (old.Hub != hub || old.Command != command || old.Assignment != actor.Id)
                return false;
            result = old.Result;
            failure = result.Failure;
            return true;
        }
        Update(0);
        if (!CanMutateCampaign(command.Generation) ||
            !_assignments.TryGetActor(session, command.Generation, command.AssignmentRevision, out actor) ||
            !HasHubAuthority(actor, hub, command))
            return false;
        if (!TryGetWorkingHub(hub, command.Generation, out var binding, out var market))
        {
            failure = SWGlobalWarHubFailure.Unavailable;
            return false;
        }
        if (!CanReachHub(actor.Body, hub, binding))
        {
            failure = SWGlobalWarHubFailure.OutOfRange;
            return false;
        }
        if (_hubWork.Values.Any(w => w.Result.Status == SWGlobalWarHubStatus.Working &&
                (w.Hub == hub || w.Body == actor.Body)))
        {
            failure = SWGlobalWarHubFailure.Busy;
            return false;
        }
        if (market.HubCapacity != command.ExpectedCapacity)
        {
            failure = SWGlobalWarHubFailure.CapacityChanged;
            return false;
        }
        if (!TryGetHubUpgrade(market, out var capacity, out var price))
        {
            failure = SWGlobalWarHubFailure.MaximumCapacity;
            return false;
        }
        if (!HasComp<DoAfterComponent>(actor.Body) || !_campaign.TryGetCampaign(out var campaign))
            return false;
        if (command.MoneyOrder != Guid.Empty &&
            (!_hubWorkGrants.TryGetValue(command.WorkGrant, out var timedGrant) ||
                !_ledger.TryGetReservation(command.MoneyOrder, out var timedOrder) ||
                timedGrant.Expires <= _campaign.Elapsed(campaign) + HubUpgradeDuration ||
                timedOrder.Expires <= _campaign.Elapsed(campaign) + HubUpgradeDuration))
        {
            failure = SWGlobalWarHubFailure.InsufficientDuration;
            return false;
        }
        _hubMutating = true;
        try
        {
            using var notifications = _ledger.DeferNotifications();
            var order = command.MoneyOrder;
            var direct = order == Guid.Empty;
            var allocation = Guid.NewGuid();
            if (direct)
            {
                if (!HasLogisticsAuthority(actor, command.HeadDecision) ||
                    !_ledger.TryExecuteSystem(new(command.Generation, Guid.NewGuid(), 0,
                        SWGlobalWarLedgerAction.Reserve, actor.Faction, SWGlobalWarAccount.Logistics, price,
                        Purpose: "hub-upgrade:" + market.Planet), out order, actor.User))
                {
                    failure = SWGlobalWarHubFailure.Funding;
                    return false;
                }
            }
            else if (!ValidHubMoneyOrder(actor, command, price))
            {
                failure = SWGlobalWarHubFailure.Funding;
                return false;
            }
            if (!_ledger.TryAllocateReservation(command.Generation, allocation, order, price, "hub-upgrade:" + market.Planet))
            {
                if (direct)
                    ReleaseHubParent(command.Generation, actor.Faction, order);
                failure = SWGlobalWarHubFailure.Funding;
                return false;
            }
            var started = _campaign.Elapsed(campaign);
            _ledger.TryGetReservation(order, out var funding);
            result = new(command.Request, SWGlobalWarHubStatus.Working, SWGlobalWarHubFailure.None,
                market.HubCapacity, capacity, price, started, started + HubUpgradeDuration, false,
                order, direct, funding.Purpose, funding.Expires);
            var work = new SWGlobalWarHubWork(hub, binding.Planet, session, actor.Id, actor.Body, actor.Faction,
                actor.Role, command, order, allocation, direct, result);
            _hubWork.Add(key, work);
            var doAfter = new DoAfterArgs(EntityManager, actor.Body, HubUpgradeDuration,
                new SWGlobalWarHubDoAfterEvent(), hub, target: hub)
            {
                BreakOnMove = false, BreakOnDamage = false, DistanceThreshold = 2f,
                NeedHand = true, BreakOnHandChange = false, BreakOnDropItem = false,
                BlockDuplicate = true, CancelDuplicate = false,
            };
            if (!_hubDoAfter.TryStartDoAfter(doAfter, out var id))
            {
                CancelHubWork(work, SWGlobalWarHubFailure.Interrupted);
                result = work.Result;
                failure = result.Failure;
                return false;
            }
            work.DoAfter = id;
            RecordHubResult(work);
            if (actor.Role == SWGlobalWarRole.Head && command.HeadDecision && _assignments.TryGetRegistry(out var registry))
            {
                foreach (var minister in registry.Comp.Assignments.Values.Where(a =>
                             a.Faction == actor.Faction && a.Role == SWGlobalWarRole.LogisticsMinister))
                    if (TryComp<ActorComponent>(minister.Body, out var player))
                        _economyChat.DispatchServerMessage(player.PlayerSession,
                            Loc.GetString("sw-global-war-hub-head-decision", ("planet", _prototypes.Index(
                                Comp<SWGlobalWarPlanetComponent>(binding.Planet).Prototype).Name)));
            }
            failure = SWGlobalWarHubFailure.None;
            return true;
        }
        finally
        {
            _hubMutating = false;
        }
    }

    /// <summary>Cancellation addresses an existing attempt. A completed attempt returns its paid result unchanged.</summary>
    public bool TryCancelHubUpgrade(ICommonSession session, EntityUid hub, Guid generation, long revision, Guid request,
        out SWGlobalWarHubResult? result)
    {
        result = null;
        if (!CanMutateCampaign(generation) || !_assignments.TryGetActor(session, generation, revision, out var actor) ||
            !_hubWork.TryGetValue((actor.User, request), out var work) || work.Hub != hub || work.Assignment != actor.Id ||
            !CanReadHubWork(actor, work))
            return false;
        // Cancellation wins if completion has not committed yet. It does not finish a due attempt itself.
        if (work.Result.Status == SWGlobalWarHubStatus.Working)
            CancelHubWork(work, SWGlobalWarHubFailure.CancelledByUser);
        result = work.Result;
        return true;
    }

    private void UpdateHubWork()
    {
        if (_hubMutating || _settling || _campaignStep.IsClosing)
            return;
        _hubMutating = true;
        try
        {
            using var notifications = _ledger.DeferNotifications();
            foreach (var work in _hubWork.Values.Where(w => w.Result.Status == SWGlobalWarHubStatus.Working).ToArray())
            {
                var failure = ValidateHubWork(work, out var market);
                if (failure != SWGlobalWarHubFailure.None)
                {
                    CancelHubWork(work, failure);
                    continue;
                }
                if (!work.ProgressFinished || !_campaign.TryGetCampaign(out var campaign) ||
                    _campaign.Elapsed(campaign) < work.Result.Finishes)
                    continue;
                var commit = new SWGlobalWarLedgerCommand(work.Command.Generation, work.Payment, 0,
                    SWGlobalWarLedgerAction.Commit, work.Faction, SWGlobalWarAccount.Logistics, work.Result.Price,
                    Reservation: work.Order, Purpose: "hub-upgrade:" + market.Planet, Allocation: work.Allocation);
                if (!_ledger.TryExecuteSystem(commit, out _, work.Session.UserId))
                {
                    CancelHubWork(work, SWGlobalWarHubFailure.Funding);
                    continue;
                }
                // All failure branches precede the debit; no callback observes payment without level and receipt.
                market.HubCapacity = work.Result.Capacity;
                work.Result = work.Result with
                {
                    Status = SWGlobalWarHubStatus.Completed, FundingState = SWGlobalWarHubFundingState.Spent,
                };
                RecordHubResult(work);
            }
        }
        finally
        {
            _hubMutating = false;
        }
    }

    private SWGlobalWarHubFailure ValidateHubWork(SWGlobalWarHubWork work, out SWGlobalWarMarketState market)
    {
        market = default!;
        if (!_campaign.TryGetCampaign(out var campaign) || campaign.Comp.Generation != work.Command.Generation ||
            campaign.Comp.Phase == SWGlobalWarCampaignPhase.Ending)
            return SWGlobalWarHubFailure.CampaignEnded;
        if (!_assignments.TryGetActor(work.Session, work.Command.Generation, work.Command.AssignmentRevision, out var actor) ||
            actor.Id != work.Assignment || actor.Body != work.Body || !HasHubAuthority(actor, work.Hub, work.Command))
            return SWGlobalWarHubFailure.AuthorityChanged;
        if (!TryGetWorkingHub(work.Hub, work.Command.Generation, out var binding, out market))
            return SWGlobalWarHubFailure.Unavailable;
        if (!CanReachHub(actor.Body, work.Hub, binding))
            return SWGlobalWarHubFailure.OutOfRange;
        if (market.HubCapacity != work.Result.PreviousCapacity ||
            !TryGetHubUpgrade(market, out var capacity, out var price) ||
            capacity != work.Result.Capacity || price != work.Result.Price)
            return SWGlobalWarHubFailure.CapacityChanged;
        if (!work.Direct && !ValidHubMoneyOrder(actor, work.Command, price) ||
            !_ledger.IsAllocationActive(work.Command.Generation, work.Allocation, work.Order, price))
            return SWGlobalWarHubFailure.Funding;
        return SWGlobalWarHubFailure.None;
    }

    private void CancelHubWork(SWGlobalWarHubWork work, SWGlobalWarHubFailure failure)
    {
        if (work.Result.Status != SWGlobalWarHubStatus.Working)
            return;
        using var notifications = _ledger.DeferNotifications();
        var released = _ledger.TryReleaseAllocation(work.Command.Generation, work.Allocation);
        if (work.Direct && released)
            released = ReleaseHubParent(work.Command.Generation, work.Faction, work.Order) ||
                _ledger.TryGetLedger(out _) && !_ledger.TryGetReservation(work.Order, out _);
        var fundingState = !released ? SWGlobalWarHubFundingState.CleanupPending :
            work.Direct ? SWGlobalWarHubFundingState.DirectReleased :
            !_ledger.TryGetReservation(work.Order, out var parent) ? SWGlobalWarHubFundingState.ParentClosed :
            _campaign.TryGetCampaign(out var campaign) && parent.Expires > _campaign.Elapsed(campaign) &&
            parent.Employee is { } employee && _assignments.TryGetAssignment(employee, out var current) &&
            current.Id == parent.Assignment ? SWGlobalWarHubFundingState.OrderAvailable : SWGlobalWarHubFundingState.ParentUnavailable;
        work.Result = work.Result with
        {
            Status = SWGlobalWarHubStatus.Cancelled, Failure = failure, FundingReleased = released,
            Capacity = work.Result.PreviousCapacity,
            FundingState = fundingState,
        };
        RecordHubResult(work);
        _hubDoAfter.Cancel(work.DoAfter);
    }

    private void RecordHubResult(SWGlobalWarHubWork work)
    {
        _hubJournal.Add(new(work.Command.Generation, work.Command.Request, work.Session.UserId, work.Assignment,
            work.Faction, work.Role, work.Command.HeadDecision, work.Planet, work.Order, work.Result));
    }

    private bool ReleaseHubParent(Guid generation, SWGlobalWarFaction faction, Guid order)
    {
        return _ledger.TryExecuteSystem(new(generation, Guid.NewGuid(), 0, SWGlobalWarLedgerAction.Release,
            faction, SWGlobalWarAccount.Logistics, Reservation: order, Purpose: "hub-work-cancelled"), out _);
    }

    private void OnHubOwnershipChanged(ref SWGlobalWarOwnershipChangedEvent args)
    {
        if (args.Location != SWGlobalWarPlanetLocation.Orbit)
            return;
        var planet = args.Planet;
        foreach (var work in _hubWork.Values.Where(w => w.Planet == planet).ToArray())
            CancelHubWork(work, SWGlobalWarHubFailure.OrbitChanged);
    }

    private void OnHubDoAfter(Entity<SWGlobalWarHubComponent> ent, ref SWGlobalWarHubDoAfterEvent args)
    {
        var user = args.User;
        var index = args.DoAfter.Index;
        var cancelled = args.Cancelled;
        foreach (var work in _hubWork.Values.Where(w => w.Hub == ent.Owner && w.Body == user &&
                     (w.DoAfter == null || w.DoAfter.Value.Index == index)).ToArray())
        {
            if (cancelled)
            {
                var failure = ValidateHubWork(work, out _);
                CancelHubWork(work, failure == SWGlobalWarHubFailure.None ? SWGlobalWarHubFailure.Interrupted : failure);
            }
            else
                work.ProgressFinished = true;
        }
    }

    private void ResetHubWork()
    {
        foreach (var work in _hubWork.Values.ToArray())
            CancelHubWork(work, SWGlobalWarHubFailure.CampaignEnded);
        _hubWork.Clear();
        _hubWorkGrants.Clear();
        _hubGrantRequests.Clear();
        _hubRevokeRequests.Clear();
        _hubJournal.Clear();
    }
}

internal sealed class SWGlobalWarHubWork(EntityUid hub, EntityUid planet, ICommonSession session, Guid assignment,
    EntityUid body, SWGlobalWarFaction faction, SWGlobalWarRole role, SWGlobalWarHubCommand command, Guid order, Guid allocation,
    bool direct, SWGlobalWarHubResult result)
{
    public readonly EntityUid Hub = hub;
    public readonly EntityUid Planet = planet;
    public readonly ICommonSession Session = session;
    public readonly Guid Assignment = assignment;
    public readonly EntityUid Body = body;
    public readonly SWGlobalWarFaction Faction = faction;
    public readonly SWGlobalWarRole Role = role;
    public readonly SWGlobalWarHubCommand Command = command;
    public readonly Guid Order = order;
    public readonly Guid Allocation = allocation;
    public readonly bool Direct = direct;
    public readonly Guid Payment = Guid.NewGuid();
    public SWGlobalWarHubResult Result = result;
    public DoAfterId? DoAfter;
    public bool ProgressFinished;
}

public sealed record SWGlobalWarHubJournalEntry(Guid Generation, Guid Request, NetUserId Author, Guid Assignment,
    SWGlobalWarFaction Faction, SWGlobalWarRole Role, bool HeadDecision, EntityUid Planet, Guid MoneyOrder,
    SWGlobalWarHubResult Result);
