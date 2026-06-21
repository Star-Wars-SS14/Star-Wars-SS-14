using System.Linq;
using Content.Server.Popups;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Sectors;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server._SW._Senate;

public sealed class SWSenateVoteConsoleSystem : EntitySystem
{
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly SWSenateStateSystem _senate = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;

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
    }

    private SWSenatePosition? GetCardPosition(Entity<SWSenateVoteConsoleComponent> ent)
    {
        var card = _itemSlots.GetItemOrNull(ent.Owner, ent.Comp.IdSlot);
        if (card == null || !TryComp<SWSenatePositionCardComponent>(card, out var position))
            return null;

        return position.Position;
    }

    private void OnEndVote(Entity<SWSenateVoteConsoleComponent> ent, ref SWSenateEndVoteMessage args)
    {
        if (GetCardPosition(ent) != SWSenatePosition.Speaker)
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
        if (GetCardPosition(ent) != SWSenatePosition.Chancellor)
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
        var position = GetCardPosition(ent);

        var activeSession = _senate.GetActiveSession();
        if (activeSession != null)
            state.SessionInfo = $"Заседание {activeSession.StartHour:00}:00–{activeSession.EndHour:00}:00";

        var bill = _senate.GetCurrentBill();
        if (bill != null)
        {
            state.HasCurrent = true;
            state.AwaitingTiebreak = bill.AwaitingTiebreak;
            state.CanVote = voterSector != null && !bill.AwaitingTiebreak;
            state.CanEndVote = position == SWSenatePosition.Speaker && _senate.CanEndCurrentVote();
            state.CanTiebreak = position == SWSenatePosition.Chancellor && bill.AwaitingTiebreak;
            state.CurrentId = bill.Id;
            state.CurrentName = bill.Name;
            state.CurrentAuthor = bill.Author;
            state.CurrentDescription = bill.Description;
            state.CurrentEffects = _senate.BuildBillEffects(bill);
            state.VotesFor = bill.Votes.Values.Count(v => v == SWVoteChoice.For);
            state.VotesAgainst = bill.Votes.Values.Count(v => v == SWVoteChoice.Against);
            state.VotesAbstain = bill.Votes.Values.Count(v => v == SWVoteChoice.Abstain);

            if (voterSector != null && bill.Votes.TryGetValue(voterSector, out var myVote))
            {
                state.HasVoted = true;
                state.MyVote = myVote;
            }

            foreach (var (sectorId, _) in _senate.SectorHolders)
            {
                if (!_proto.TryIndex<SWSectorPrototype>(sectorId, out var sectorProto))
                    continue;

                state.SectorVotes.Add(new SWSectorVoteEntry
                {
                    Name = sectorProto.Name,
                    Color = sectorProto.Color,
                    VoteLabel = bill.Votes.TryGetValue(sectorId, out var choice)
                        ? Loc.GetString(VoteLoc(choice))
                        : Loc.GetString("sw-senate-vote-none"),
                });
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
