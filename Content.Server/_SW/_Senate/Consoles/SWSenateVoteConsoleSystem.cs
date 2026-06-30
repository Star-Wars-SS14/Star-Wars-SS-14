using System.Linq;
using Content.Server.Popups;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Sectors;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Content.Server._SW._Senate.Core;
using Content.Server._SW._Senate.Debate;

namespace Content.Server._SW._Senate.Consoles;

public sealed class SWSenateVoteConsoleSystem : EntitySystem
{
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly SWSenateStateSystem _senate = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly SWSenateFloorSystem _floor = default!;
    [Dependency] private readonly SWSenatePartySystem _party = default!;

    private const float UpdateInterval = 3f;
    private float _accumulator;

    public override void Initialize()
    {
        SubscribeLocalEvent<SWSenateVoteConsoleComponent, BoundUIOpenedEvent>(OnOpened);
        SubscribeLocalEvent<SWSenateVoteConsoleComponent, EntInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<SWSenateVoteConsoleComponent, EntRemovedFromContainerMessage>(OnRemoved);
        SubscribeLocalEvent<SWSenateVoteConsoleComponent, SWSenateCastVoteMessage>(OnVote);
        SubscribeLocalEvent<SWSenateVoteConsoleComponent, SWSenateEndVoteMessage>(OnEndVote);
        SubscribeLocalEvent<SWSenateVoteConsoleComponent, SWSenateTiebreakMessage>(OnTiebreak);
        SubscribeLocalEvent<SWSenateVoteConsoleComponent, SWSenateGrantFloorMessage>(OnGrantFloor);
        SubscribeLocalEvent<SWSenateVoteConsoleComponent, SWSenateRevokeFloorMessage>(OnRevokeFloor);
        SubscribeLocalEvent<SWSenateVoteConsoleComponent, SWSenateRemoveFromQueueMessage>(OnRemoveFromQueue);
        SubscribeLocalEvent<SWSenateVoteConsoleComponent, SWSenateGrantChancellorMessage>(OnGrantChancellor);
        SubscribeLocalEvent<SWSenateVoteConsoleComponent, SWSenatePartyWhipMessage>(OnPartyWhip);
    }

    private void OnPartyWhip(Entity<SWSenateVoteConsoleComponent> ent, ref SWSenatePartyWhipMessage args)
    {
        var sectorId = GetVoterSector(ent);
        if (sectorId == null)
        {
            _popup.PopupCursor(Loc.GetString("sw-senate-vote-cant-vote"), args.Actor, PopupType.Medium);
            return;
        }

        if (_party.TryWhip(sectorId, out var reason))
            _popup.PopupCursor(Loc.GetString("sw-senate-whip-done"), args.Actor, PopupType.Medium);
        else
            _popup.PopupCursor(Loc.GetString(reason), args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnGrantFloor(Entity<SWSenateVoteConsoleComponent> ent, ref SWSenateGrantFloorMessage args)
    {
        if (!ActsAsSpeaker(ent))
            return;

        var target = GetEntity(args.Target);
        if (!Exists(target))
            return;

        _floor.GrantFloor(target);
        _popup.PopupCursor(Loc.GetString("sw-senate-floor-granted"), args.Actor, PopupType.Medium);
        UpdateUi(ent);
    }

    private void OnRevokeFloor(Entity<SWSenateVoteConsoleComponent> ent, ref SWSenateRevokeFloorMessage args)
    {
        if (!ActsAsSpeaker(ent))
            return;

        _floor.RevokeFloor();
        _popup.PopupCursor(Loc.GetString("sw-senate-floor-revoked"), args.Actor, PopupType.Medium);
        UpdateUi(ent);
    }

    private void OnRemoveFromQueue(Entity<SWSenateVoteConsoleComponent> ent, ref SWSenateRemoveFromQueueMessage args)
    {
        if (!ActsAsSpeaker(ent))
            return;

        if (_floor.RemoveFromQueue(GetEntity(args.Target)))
            _popup.PopupCursor(Loc.GetString("sw-senate-floor-queue-removed"), args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnGrantChancellor(Entity<SWSenateVoteConsoleComponent> ent, ref SWSenateGrantChancellorMessage args)
    {
        if (!ActsAsSpeaker(ent))
            return;

        _floor.GrantChancellor();
        _popup.PopupCursor(Loc.GetString("sw-senate-floor-granted"), args.Actor, PopupType.Medium);
        UpdateUi(ent);
    }

    private SWSenatePosition? GetCardPosition(Entity<SWSenateVoteConsoleComponent> ent)
    {
        var card = _itemSlots.GetItemOrNull(ent.Owner, ent.Comp.IdSlot);
        if (card == null || !TryComp<SWSenatePositionCardComponent>(card, out var position))
            return null;

        return position.Position;
    }

    // A lone presiding officer covers the vacant counterpart: the Chancellor may chair (end votes,
    // run the floor) when there is no Speaker, and the Speaker may break ties when there is no Chancellor.
    private bool ActsAsSpeaker(Entity<SWSenateVoteConsoleComponent> ent) => GetCardPosition(ent) switch
    {
        SWSenatePosition.Speaker => _senate.Speaker != null,
        SWSenatePosition.Chancellor => _senate.Chancellor != null && _senate.Speaker == null,
        _ => false,
    };

    private bool ActsAsChancellor(Entity<SWSenateVoteConsoleComponent> ent) => GetCardPosition(ent) switch
    {
        SWSenatePosition.Chancellor => _senate.Chancellor != null,
        SWSenatePosition.Speaker => _senate.Speaker != null && _senate.Chancellor == null,
        _ => false,
    };

    private void OnEndVote(Entity<SWSenateVoteConsoleComponent> ent, ref SWSenateEndVoteMessage args)
    {
        if (!ActsAsSpeaker(ent))
        {
            _popup.PopupCursor(Loc.GetString("sw-senate-vote-only-speaker"), args.Actor, PopupType.Medium);
            return;
        }

        if (_senate.EndVoteBySpeaker())
            _popup.PopupCursor(Loc.GetString("sw-senate-vote-ended"), args.Actor, PopupType.Medium);
        else
            _popup.PopupCursor(Loc.GetString("sw-senate-vote-cant-end"), args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnTiebreak(Entity<SWSenateVoteConsoleComponent> ent, ref SWSenateTiebreakMessage args)
    {
        if (!ActsAsChancellor(ent))
        {
            _popup.PopupCursor(Loc.GetString("sw-senate-vote-only-chancellor"), args.Actor, PopupType.Medium);
            return;
        }

        if (_senate.Tiebreak(args.Choice))
            _popup.PopupCursor(Loc.GetString("sw-senate-vote-tiebreak-done"), args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnOpened(EntityUid uid, SWSenateVoteConsoleComponent comp, BoundUIOpenedEvent args)
    {
        UpdateUi((uid, comp));
    }

    private void OnInserted(Entity<SWSenateVoteConsoleComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID == ent.Comp.IdSlot)
            UpdateUi(ent);
    }

    private void OnRemoved(Entity<SWSenateVoteConsoleComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID == ent.Comp.IdSlot)
            UpdateUi(ent);
    }

    public override void Update(float frameTime)
    {
        _accumulator += frameTime;
        if (_accumulator < UpdateInterval)
            return;

        _accumulator = 0f;

        var query = EntityQueryEnumerator<SWSenateVoteConsoleComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (_ui.IsUiOpen(uid, SWSenateVoteConsoleUiKey.Key))
                UpdateUi((uid, comp));
        }
    }

    private void OnVote(Entity<SWSenateVoteConsoleComponent> ent, ref SWSenateCastVoteMessage args)
    {
        var sectorId = GetVoterSector(ent);
        if (sectorId == null)
        {
            _popup.PopupCursor(Loc.GetString("sw-senate-vote-cant-vote"), args.Actor, PopupType.Medium);
            return;
        }

        if (_senate.GetCurrentBill() == null)
        {
            _popup.PopupCursor(Loc.GetString("sw-senate-vote-no-bill"), args.Actor, PopupType.Medium);
            return;
        }

        if (_senate.CastVote(sectorId, args.Choice))
            _popup.PopupCursor(Loc.GetString("sw-senate-vote-cast"), args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private string? GetVoterSector(Entity<SWSenateVoteConsoleComponent> ent)
    {
        var card = _itemSlots.GetItemOrNull(ent.Owner, ent.Comp.IdSlot);
        if (card == null
            || !TryComp<SWSenatePositionCardComponent>(card, out var position)
            || position.Position != SWSenatePosition.Senator)
            return null;

        if (!TryComp<SWSenateSectorComponent>(card, out var sectorComp) || sectorComp.Sector == null)
            return null;

        return _senate.IsClaimed(sectorComp.Sector) ? sectorComp.Sector : null;
    }

    private void UpdateUi(Entity<SWSenateVoteConsoleComponent> ent)
    {
        var state = new SWSenateVoteConsoleState
        {
            Bills = _senate.BuildBillEntries(),
            GameTime = _time.TimeString(),
        };

        var voterSector = GetVoterSector(ent);

        if (ActsAsSpeaker(ent))
        {
            state.CanControlFloor = true;
            if (_floor.FloorHolder is { } holder && Exists(holder))
                state.FloorHolderName = Name(holder);

            foreach (var requester in _floor.Queue)
            {
                if (!Exists(requester))
                    continue;

                state.FloorQueue.Add(new SWFloorQueueEntry
                {
                    Target = GetNetEntity(requester),
                    Name = Name(requester),
                    IsChancellor = requester == _floor.Chancellor,
                });
            }
        }

        var activeSession = _senate.GetActiveSession();
        if (activeSession != null)
            state.SessionInfo = $"Заседание {activeSession.StartHour:00}:00–{activeSession.EndHour:00}:00";

        var bill = _senate.GetCurrentBill();
        if (bill != null)
        {
            state.HasCurrent = true;
            state.AwaitingTiebreak = bill.AwaitingTiebreak;
            state.CanVote = voterSector != null && !bill.AwaitingTiebreak;
            state.CanEndVote = ActsAsSpeaker(ent) && _senate.CanEndCurrentVote();
            state.CanTiebreak = ActsAsChancellor(ent) && bill.AwaitingTiebreak;
            state.CurrentId = bill.Id;
            state.CurrentName = bill.Name;
            state.CurrentAuthor = bill.Author;
            state.CurrentDescription = bill.Description;
            state.CurrentEffects = _senate.BuildBillEffects(bill);
            state.VotesFor = bill.Votes.Values.Count(v => v == SWVoteChoice.For);
            state.VotesAgainst = bill.Votes.Values.Count(v => v == SWVoteChoice.Against);
            state.VotesAbstain = bill.Votes.Values.Count(v => v == SWVoteChoice.Abstain);
            state.VoteNeeded = _senate.VoteNeeded(bill);
            state.VoteThreshold = bill.RequiredPercent;

            if (voterSector != null && bill.Votes.TryGetValue(voterSector, out var myVote))
            {
                state.HasVoted = true;
                state.MyVote = myVote;
            }

            if (voterSector != null && _party.CanWhip(voterSector))
            {
                state.CanPartyWhip = true;
                state.PartyWhipLabel = Loc.GetString("sw-senate-whip-action");
            }

            foreach (var (sectorId, _) in _senate.SectorHolders)
            {
                if (!_proto.TryIndex<SWSectorPrototype>(sectorId, out var sectorProto))
                    continue;

                var voteEntry = new SWSectorVoteEntry
                {
                    Name = sectorProto.Name,
                    Color = sectorProto.Color,
                    VoteLabel = bill.Votes.TryGetValue(sectorId, out var choice)
                        ? Loc.GetString(VoteLoc(choice))
                        : Loc.GetString("sw-senate-vote-none"),
                };

                if (_party.GetBlocTag(sectorId) is { } bloc)
                {
                    voteEntry.PartyTag = bloc.Name;
                    voteEntry.PartyColor = bloc.Color;
                }

                state.SectorVotes.Add(voteEntry);
            }

            state.SectorVotes.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
        }

        _ui.SetUiState(ent.Owner, SWSenateVoteConsoleUiKey.Key, state);
    }

    private static string VoteLoc(SWVoteChoice choice) => choice switch
    {
        SWVoteChoice.For => "sw-senate-vote-for",
        SWVoteChoice.Against => "sw-senate-vote-against",
        SWVoteChoice.Abstain => "sw-senate-vote-abstain",
        _ => "sw-senate-vote-none",
    };
}
