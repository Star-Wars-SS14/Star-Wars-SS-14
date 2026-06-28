using System.Linq;
using Content.Server.Popups;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Sectors;
using Content.Shared.Access.Systems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Content.Server._SW._Senate.Core;
using Content.Server._SW._Senate.Debate;
using Content.Server._SW._Senate.Economy;
using Content.Shared._SW._Senate.Bills;

namespace Content.Server._SW._Senate.Consoles;

public sealed class SWSenateWorkConsoleSystem : EntitySystem
{
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly SWSenateStateSystem _senate = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly SharedIdCardSystem _idCard = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly SWSenateFloorSystem _floor = default!;
    [Dependency] private readonly SWSenateDilemmaSystem _dilemma = default!;
    [Dependency] private readonly SWSenateBudgetSystem _budget = default!;
    [Dependency] private readonly SWSenateProgramSystem _program = default!;
    [Dependency] private readonly SWSenateAmbitionSystem _ambition = default!;
    [Dependency] private readonly SWSenateCrisisSystem _crisis = default!;
    [Dependency] private readonly SWMinistryDefenseSystem _defense = default!;
    [Dependency] private readonly SWSenatePartySystem _party = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private const float UpdateInterval = 3f;
    private static readonly TimeSpan AnnounceCooldown = TimeSpan.FromSeconds(12);
    private float _accumulator;
    private TimeSpan _nextAnnounce;

    public override void Initialize()
    {
        SubscribeLocalEvent<SWSenateWorkConsoleComponent, BoundUIOpenedEvent>(OnOpened);
        SubscribeLocalEvent<SWSenateWorkConsoleComponent, EntInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<SWSenateWorkConsoleComponent, EntRemovedFromContainerMessage>(OnRemoved);
        SubscribeLocalEvent<SWSenateWorkConsoleComponent, SWSenateTakeOfficeMessage>(OnTakeOffice);
        SubscribeLocalEvent<SWSenateWorkConsoleComponent, SWSenateResignMessage>(OnResign);
        SubscribeLocalEvent<SWSenateWorkConsoleComponent, SWSenateSubmitBillMessage>(OnSubmitBill);
        SubscribeLocalEvent<SWSenateWorkConsoleComponent, SWSenateSubmitPresetMessage>(OnSubmitPreset);
        SubscribeLocalEvent<SWSenateWorkConsoleComponent, SWSenateResolveDilemmaMessage>(OnResolveDilemma);
        SubscribeLocalEvent<SWSenateWorkConsoleComponent, SWSenateApplyProgramMessage>(OnApplyProgram);
        SubscribeLocalEvent<SWSenateWorkConsoleComponent, SWSenateResolveCrisisMessage>(OnResolveCrisis);
        SubscribeLocalEvent<SWSenateWorkConsoleComponent, SWSenateAidCrisisMessage>(OnAidCrisis);
        SubscribeLocalEvent<SWSenateWorkConsoleComponent, SWSenateProposeEmergencyLawMessage>(OnProposeEmergencyLaw);
        SubscribeLocalEvent<SWSenateWorkConsoleComponent, SWSenateSetBudgetMessage>(OnSetBudget);
        SubscribeLocalEvent<SWSenateWorkConsoleComponent, SWSenateLockBudgetMessage>(OnLockBudget);
        SubscribeLocalEvent<SWSenateWorkConsoleComponent, SWSenateScheduleSessionMessage>(OnScheduleSession);
        SubscribeLocalEvent<SWSenateWorkConsoleComponent, SWSenateRemoveSessionMessage>(OnRemoveSession);
        SubscribeLocalEvent<SWSenateWorkConsoleComponent, SWSenateEmergencySessionMessage>(OnEmergencySession);
        SubscribeLocalEvent<SWSenateWorkConsoleComponent, SWSenateConfirmSessionMessage>(OnConfirmSession);
        SubscribeLocalEvent<SWSenateWorkConsoleComponent, SWDefenseApproveWarMessage>(OnApproveWar);
        SubscribeLocalEvent<SWSenateWorkConsoleComponent, SWDefenseRejectWarMessage>(OnRejectWar);
        SubscribeLocalEvent<SWSenateWorkConsoleComponent, SWSenateRejectSessionMessage>(OnRejectSession);
        SubscribeLocalEvent<SWSenateWorkConsoleComponent, SWSenateAnnounceMessage>(OnAnnounce);
        SubscribeLocalEvent<SWSenateWorkConsoleComponent, SWSenateCreatePartyMessage>(OnCreateParty);
        SubscribeLocalEvent<SWSenateWorkConsoleComponent, SWSenateRequestJoinPartyMessage>(OnRequestJoinParty);
        SubscribeLocalEvent<SWSenateWorkConsoleComponent, SWSenateCancelJoinPartyMessage>(OnCancelJoinParty);
        SubscribeLocalEvent<SWSenateWorkConsoleComponent, SWSenateConfirmJoinPartyMessage>(OnConfirmJoinParty);
        SubscribeLocalEvent<SWSenateWorkConsoleComponent, SWSenateRejectJoinPartyMessage>(OnRejectJoinParty);
        SubscribeLocalEvent<SWSenateWorkConsoleComponent, SWSenateLeavePartyMessage>(OnLeaveParty);
        SubscribeLocalEvent<SWSenateWorkConsoleComponent, SWSenateDisbandPartyMessage>(OnDisbandParty);
        SubscribeLocalEvent<SWSenateWorkConsoleComponent, SWSenateInitiateLeaderVoteMessage>(OnInitiateLeaderVote);
        SubscribeLocalEvent<SWSenateWorkConsoleComponent, SWSenateInitiateEvictVoteMessage>(OnInitiateEvictVote);
        SubscribeLocalEvent<SWSenateWorkConsoleComponent, SWSenateCastLeaderVoteMessage>(OnCastLeaderVote);
    }

    private void OnInitiateEvictVote(Entity<SWSenateWorkConsoleComponent> ent, ref SWSenateInitiateEvictVoteMessage args)
    {
        var sectorId = GetSectorOwner(ent);
        if (sectorId == null)
            return;

        if (_party.InitiateEvict(sectorId, args.PartyId, args.SectorId))
            _popup.PopupCursor(Loc.GetString("sw-senate-party-evict-started"), args.Actor, PopupType.Medium);
        else
            _popup.PopupCursor(Loc.GetString("sw-senate-party-failed"), args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnCreateParty(Entity<SWSenateWorkConsoleComponent> ent, ref SWSenateCreatePartyMessage args)
    {
        var sectorId = GetSectorOwner(ent);
        if (sectorId == null)
        {
            _popup.PopupCursor(Loc.GetString("sw-senate-work-senator-hint"), args.Actor, PopupType.Medium);
            return;
        }

        if (_party.CreateParty(sectorId, args.Name, args.Description))
            _popup.PopupCursor(Loc.GetString("sw-senate-party-created"), args.Actor, PopupType.Medium);
        else
            _popup.PopupCursor(Loc.GetString("sw-senate-party-failed"), args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnRequestJoinParty(Entity<SWSenateWorkConsoleComponent> ent, ref SWSenateRequestJoinPartyMessage args)
    {
        var sectorId = GetSectorOwner(ent);
        if (sectorId == null)
            return;

        if (_party.RequestJoin(sectorId, args.PartyId))
            _popup.PopupCursor(Loc.GetString("sw-senate-party-join-requested"), args.Actor, PopupType.Medium);
        else
            _popup.PopupCursor(Loc.GetString("sw-senate-party-failed"), args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnCancelJoinParty(Entity<SWSenateWorkConsoleComponent> ent, ref SWSenateCancelJoinPartyMessage args)
    {
        var sectorId = GetSectorOwner(ent);
        if (sectorId == null)
            return;

        _party.CancelJoin(sectorId, args.PartyId);
        UpdateUi(ent);
    }

    private void OnConfirmJoinParty(Entity<SWSenateWorkConsoleComponent> ent, ref SWSenateConfirmJoinPartyMessage args)
    {
        var sectorId = GetSectorOwner(ent);
        if (sectorId == null)
            return;

        if (_party.ConfirmJoin(sectorId, args.PartyId, args.SectorId))
            _popup.PopupCursor(Loc.GetString("sw-senate-party-member-added"), args.Actor, PopupType.Medium);
        else
            _popup.PopupCursor(Loc.GetString("sw-senate-party-failed"), args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnRejectJoinParty(Entity<SWSenateWorkConsoleComponent> ent, ref SWSenateRejectJoinPartyMessage args)
    {
        var sectorId = GetSectorOwner(ent);
        if (sectorId == null)
            return;

        _party.RejectJoin(sectorId, args.PartyId, args.SectorId);
        UpdateUi(ent);
    }

    private void OnLeaveParty(Entity<SWSenateWorkConsoleComponent> ent, ref SWSenateLeavePartyMessage args)
    {
        var sectorId = GetSectorOwner(ent);
        if (sectorId == null)
            return;

        if (_party.Leave(sectorId, args.PartyId))
            _popup.PopupCursor(Loc.GetString("sw-senate-party-left"), args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnDisbandParty(Entity<SWSenateWorkConsoleComponent> ent, ref SWSenateDisbandPartyMessage args)
    {
        var sectorId = GetSectorOwner(ent);
        if (sectorId == null)
            return;

        if (_party.Disband(sectorId, args.PartyId))
            _popup.PopupCursor(Loc.GetString("sw-senate-party-disbanded"), args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnInitiateLeaderVote(Entity<SWSenateWorkConsoleComponent> ent, ref SWSenateInitiateLeaderVoteMessage args)
    {
        var sectorId = GetSectorOwner(ent);
        if (sectorId == null)
            return;

        if (_party.InitiateVote(sectorId, args.PartyId))
            _popup.PopupCursor(Loc.GetString("sw-senate-party-vote-started"), args.Actor, PopupType.Medium);
        else
            _popup.PopupCursor(Loc.GetString("sw-senate-party-failed"), args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnCastLeaderVote(Entity<SWSenateWorkConsoleComponent> ent, ref SWSenateCastLeaderVoteMessage args)
    {
        var sectorId = GetSectorOwner(ent);
        if (sectorId == null)
            return;

        _party.CastVote(sectorId, args.PartyId, args.Support);
        UpdateUi(ent);
    }

    private void OnAnnounce(Entity<SWSenateWorkConsoleComponent> ent, ref SWSenateAnnounceMessage args)
    {
        var position = GetCardPosition(ent);
        if (position != SWSenatePosition.Speaker && position != SWSenatePosition.Chancellor)
        {
            _popup.PopupCursor(Loc.GetString("sw-senate-announce-only-leaders"), args.Actor, PopupType.Medium);
            return;
        }

        var inOffice = position == SWSenatePosition.Speaker ? _senate.Speaker != null : _senate.Chancellor != null;
        if (!inOffice)
        {
            _popup.PopupCursor(Loc.GetString("sw-senate-not-in-office"), args.Actor, PopupType.Medium);
            return;
        }

        if (_timing.CurTime < _nextAnnounce)
        {
            _popup.PopupCursor(Loc.GetString("sw-senate-announce-cooldown"), args.Actor, PopupType.Medium);
            return;
        }

        var text = args.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
            return;

        var sender = Loc.GetString(position == SWSenatePosition.Speaker
            ? "sw-senate-work-position-speaker"
            : "sw-senate-work-position-chancellor");

        RaiseLocalEvent(new SWSenateCustomAnnouncementEvent(sender, text));
        _nextAnnounce = _timing.CurTime + AnnounceCooldown;
        _popup.PopupCursor(Loc.GetString("sw-senate-announce-sent"), args.Actor, PopupType.Medium);
    }

    private SWSenatePosition? GetCardPosition(Entity<SWSenateWorkConsoleComponent> ent)
    {
        var card = _itemSlots.GetItemOrNull(ent.Owner, ent.Comp.IdSlot);
        if (card == null || !TryComp<SWSenatePositionCardComponent>(card, out var position))
            return null;

        return position.Position;
    }

    // When only one of the two presiding offices is filled, that officer acts for both:
    // the lone Speaker also confirms, the lone Chancellor also schedules.
    private bool CanScheduleAs(Entity<SWSenateWorkConsoleComponent> ent) => GetCardPosition(ent) switch
    {
        SWSenatePosition.Speaker => _senate.Speaker != null,
        SWSenatePosition.Chancellor => _senate.Chancellor != null && _senate.Speaker == null,
        _ => false,
    };

    private bool CanConfirmAs(Entity<SWSenateWorkConsoleComponent> ent) => GetCardPosition(ent) switch
    {
        SWSenatePosition.Chancellor => _senate.Chancellor != null,
        SWSenatePosition.Speaker => _senate.Speaker != null && _senate.Chancellor == null,
        _ => false,
    };

    private void OnScheduleSession(Entity<SWSenateWorkConsoleComponent> ent, ref SWSenateScheduleSessionMessage args)
    {
        if (!CanScheduleAs(ent))
        {
            _popup.PopupCursor(Loc.GetString("sw-senate-schedule-only-speaker"), args.Actor, PopupType.Medium);
            return;
        }

        if (_senate.TryScheduleSession(args.BillId, args.StartHour, args.Duration) == null)
            _popup.PopupCursor(Loc.GetString("sw-senate-schedule-invalid"), args.Actor, PopupType.Medium);
        else
            _popup.PopupCursor(Loc.GetString("sw-senate-schedule-added"), args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnRemoveSession(Entity<SWSenateWorkConsoleComponent> ent, ref SWSenateRemoveSessionMessage args)
    {
        if (!CanScheduleAs(ent))
            return;

        _senate.RemoveSession(args.SessionId);
        UpdateUi(ent);
    }

    private void OnEmergencySession(Entity<SWSenateWorkConsoleComponent> ent, ref SWSenateEmergencySessionMessage args)
    {
        if (!CanScheduleAs(ent))
        {
            _popup.PopupCursor(Loc.GetString("sw-senate-schedule-only-speaker"), args.Actor, PopupType.Medium);
            return;
        }

        var result = _senate.ScheduleEmergencySession(args.BillId);
        if (result == null)
        {
            _popup.PopupCursor(Loc.GetString("sw-senate-emergency-invalid"), args.Actor, PopupType.Medium);
        }
        else
        {
            RaiseLocalEvent(new SWSenateEmergencySessionScheduledEvent(_senate.GetBillName(args.BillId), result.StartHour, result.DisplacedBill));
            _popup.PopupCursor(Loc.GetString("sw-senate-emergency-scheduled"), args.Actor, PopupType.Medium);
        }

        UpdateUi(ent);
    }

    private void OnConfirmSession(Entity<SWSenateWorkConsoleComponent> ent, ref SWSenateConfirmSessionMessage args)
    {
        if (!CanConfirmAs(ent))
        {
            _popup.PopupCursor(Loc.GetString("sw-senate-schedule-only-chancellor"), args.Actor, PopupType.Medium);
            return;
        }

        if (_senate.ConfirmSession(args.SessionId))
            _popup.PopupCursor(Loc.GetString("sw-senate-schedule-confirmed"), args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnApproveWar(Entity<SWSenateWorkConsoleComponent> ent, ref SWDefenseApproveWarMessage args)
    {
        if (!CanConfirmAs(ent))
        {
            _popup.PopupCursor(Loc.GetString("sw-senate-schedule-only-chancellor"), args.Actor, PopupType.Medium);
            return;
        }

        if (_defense.ApproveWar())
            _popup.PopupCursor(Loc.GetString("sw-defense-war-approved"), args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnRejectWar(Entity<SWSenateWorkConsoleComponent> ent, ref SWDefenseRejectWarMessage args)
    {
        if (!CanConfirmAs(ent))
            return;

        _defense.RejectWar();
        UpdateUi(ent);
    }

    private void OnRejectSession(Entity<SWSenateWorkConsoleComponent> ent, ref SWSenateRejectSessionMessage args)
    {
        if (!CanConfirmAs(ent))
        {
            _popup.PopupCursor(Loc.GetString("sw-senate-schedule-only-chancellor"), args.Actor, PopupType.Medium);
            return;
        }

        if (_senate.RejectSession(args.SessionId))
            _popup.PopupCursor(Loc.GetString("sw-senate-schedule-rejected"), args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnSubmitBill(Entity<SWSenateWorkConsoleComponent> ent, ref SWSenateSubmitBillMessage args)
    {
        var card = _itemSlots.GetItemOrNull(ent.Owner, ent.Comp.IdSlot);
        if (card == null
            || !TryComp<SWSenatePositionCardComponent>(card, out var position)
            || position.Position != SWSenatePosition.Senator)
        {
            _popup.PopupCursor(Loc.GetString("sw-senate-bill-not-senator"), args.Actor, PopupType.Medium);
            return;
        }

        if (!TryComp<SWSenateSectorComponent>(card, out var billSector) || billSector.Sector is not { } billSectorId || !_senate.IsClaimed(billSectorId))
        {
            _popup.PopupCursor(Loc.GetString("sw-senate-work-senator-hint"), args.Actor, PopupType.Medium);
            return;
        }

        var name = args.Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            _popup.PopupCursor(Loc.GetString("sw-senate-bill-no-name"), args.Actor, PopupType.Medium);
            return;
        }

        if (_senate.HasActiveBill(Name(args.Actor)))
        {
            _popup.PopupCursor(Loc.GetString("sw-senate-bill-already-active"), args.Actor, PopupType.Medium);
            return;
        }

        var effects = new Dictionary<string, float>();
        var net = 0f;
        foreach (var (statId, rawDelta) in args.Effects)
        {
            if (!_proto.HasIndex<SWStatPrototype>(statId))
                continue;

            var delta = Math.Clamp(rawDelta, -ent.Comp.MaxEffect, ent.Comp.MaxEffect);
            if (delta == 0)
                continue;

            effects[statId] = delta;
            net += delta;
        }

        if (effects.Count == 0)
        {
            _popup.PopupCursor(Loc.GetString("sw-senate-bill-no-effect"), args.Actor, PopupType.Medium);
            return;
        }

        if (net > ent.Comp.DraftBudget)
        {
            _popup.PopupCursor(Loc.GetString("sw-senate-bill-over-budget"), args.Actor, PopupType.Medium);
            return;
        }

        _senate.SubmitBill(name, args.Description.Trim(), Name(args.Actor), effects);
        _popup.PopupCursor(Loc.GetString("sw-senate-bill-submitted"), args.Actor, PopupType.Medium);
        UpdateUi(ent);
    }

    private void OnSubmitPreset(Entity<SWSenateWorkConsoleComponent> ent, ref SWSenateSubmitPresetMessage args)
    {
        var card = _itemSlots.GetItemOrNull(ent.Owner, ent.Comp.IdSlot);
        if (card == null
            || !TryComp<SWSenatePositionCardComponent>(card, out var position)
            || position.Position != SWSenatePosition.Senator)
        {
            _popup.PopupCursor(Loc.GetString("sw-senate-bill-not-senator"), args.Actor, PopupType.Medium);
            return;
        }

        if (!TryComp<SWSenateSectorComponent>(card, out var presetSector) || presetSector.Sector is not { } presetSectorId || !_senate.IsClaimed(presetSectorId))
        {
            _popup.PopupCursor(Loc.GetString("sw-senate-work-senator-hint"), args.Actor, PopupType.Medium);
            return;
        }

        if (_senate.HasActiveBill(Name(args.Actor)))
        {
            _popup.PopupCursor(Loc.GetString("sw-senate-bill-already-active"), args.Actor, PopupType.Medium);
            return;
        }

        if (!_proto.TryIndex<SWBillPresetPrototype>(args.PresetId, out var preset))
            return;

        var effects = new Dictionary<string, float>();
        foreach (var (subId, rawDelta) in preset.Effects)
        {
            if (!_proto.HasIndex<SWStatPrototype>(subId))
                continue;

            var delta = Math.Clamp(rawDelta, -ent.Comp.MaxEffect, ent.Comp.MaxEffect);
            if (delta == 0)
                continue;

            effects[subId] = delta;
        }

        if (effects.Count == 0)
            return;

        _senate.SubmitBill(preset.Name, preset.Description, Name(args.Actor), effects);
        _popup.PopupCursor(Loc.GetString("sw-senate-bill-submitted"), args.Actor, PopupType.Medium);
        UpdateUi(ent);
    }

    private string? GetDilemmaOwner(Entity<SWSenateWorkConsoleComponent> ent)
    {
        var card = _itemSlots.GetItemOrNull(ent.Owner, ent.Comp.IdSlot);
        if (card == null || !TryComp<SWSenatePositionCardComponent>(card, out var position))
            return null;

        if (position.Position == SWSenatePosition.Senator)
        {
            if (TryComp<SWSenateSectorComponent>(card, out var sector) && sector.Sector is { } claimed && _senate.IsClaimed(claimed))
                return claimed;

            return null;
        }

        if (position.Position == SWSenatePosition.Chancellor && _senate.Chancellor != null)
            return SWSenateDilemmaSystem.RepublicOwner;

        return null;
    }

    private void OnResolveDilemma(Entity<SWSenateWorkConsoleComponent> ent, ref SWSenateResolveDilemmaMessage args)
    {
        var owner = GetDilemmaOwner(ent);
        if (owner == null)
            return;

        if (_dilemma.Resolve(args.DilemmaId, args.OptionIndex, owner))
            _popup.PopupCursor(Loc.GetString("sw-senate-dilemma-resolved"), args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private string? GetSectorOwner(Entity<SWSenateWorkConsoleComponent> ent)
    {
        var card = _itemSlots.GetItemOrNull(ent.Owner, ent.Comp.IdSlot);
        if (card == null || !TryComp<SWSenatePositionCardComponent>(card, out var position) || position.Position != SWSenatePosition.Senator)
            return null;

        if (TryComp<SWSenateSectorComponent>(card, out var sector) && sector.Sector is { } claimed && _senate.IsClaimed(claimed))
            return claimed;

        return null;
    }

    private void OnApplyProgram(Entity<SWSenateWorkConsoleComponent> ent, ref SWSenateApplyProgramMessage args)
    {
        var sectorId = GetSectorOwner(ent);
        if (sectorId == null)
        {
            _popup.PopupCursor(Loc.GetString("sw-senate-work-senator-hint"), args.Actor, PopupType.Medium);
            return;
        }

        if (_program.Apply(sectorId, args.ProgramId))
            _popup.PopupCursor(Loc.GetString("sw-senate-program-applied"), args.Actor, PopupType.Medium);
        else
            _popup.PopupCursor(Loc.GetString("sw-senate-program-failed"), args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnResolveCrisis(Entity<SWSenateWorkConsoleComponent> ent, ref SWSenateResolveCrisisMessage args)
    {
        var sectorId = GetSectorOwner(ent);
        if (sectorId == null)
        {
            _popup.PopupCursor(Loc.GetString("sw-senate-work-senator-hint"), args.Actor, PopupType.Medium);
            return;
        }

        if (_crisis.ApplySolution(args.CrisisId, args.SolutionIndex, sectorId))
            _popup.PopupCursor(Loc.GetString("sw-senate-crisis-action"), args.Actor, PopupType.Medium);
        else
            _popup.PopupCursor(Loc.GetString("sw-senate-crisis-failed"), args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnAidCrisis(Entity<SWSenateWorkConsoleComponent> ent, ref SWSenateAidCrisisMessage args)
    {
        var sectorId = GetSectorOwner(ent);
        if (sectorId == null)
        {
            _popup.PopupCursor(Loc.GetString("sw-senate-work-senator-hint"), args.Actor, PopupType.Medium);
            return;
        }

        if (_crisis.AidCrisis(args.CrisisId, sectorId))
            _popup.PopupCursor(Loc.GetString("sw-senate-crisis-aided"), args.Actor, PopupType.Medium);
        else
            _popup.PopupCursor(Loc.GetString("sw-senate-crisis-failed"), args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnProposeEmergencyLaw(Entity<SWSenateWorkConsoleComponent> ent, ref SWSenateProposeEmergencyLawMessage args)
    {
        var author = Name(args.Actor);
        var sectorOwner = GetSectorOwner(ent);
        var isChancellor = ChancellorInOffice(ent);

        if (_crisis.ProposeLaw(args.CrisisId, args.LawIndex, author, sectorOwner, isChancellor))
            _popup.PopupCursor(Loc.GetString("sw-senate-crisis-law-proposed"), args.Actor, PopupType.Medium);
        else
            _popup.PopupCursor(Loc.GetString("sw-senate-crisis-failed"), args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private bool ChancellorInOffice(Entity<SWSenateWorkConsoleComponent> ent) =>
        GetCardPosition(ent) == SWSenatePosition.Chancellor && _senate.Chancellor != null;

    private void OnSetBudget(Entity<SWSenateWorkConsoleComponent> ent, ref SWSenateSetBudgetMessage args)
    {
        if (!ChancellorInOffice(ent))
            return;

        _budget.SetAllocation(args.SectorId, args.Amount);
        UpdateUi(ent);
    }

    private void OnLockBudget(Entity<SWSenateWorkConsoleComponent> ent, ref SWSenateLockBudgetMessage args)
    {
        if (!ChancellorInOffice(ent))
            return;

        if (_budget.Lock())
            _popup.PopupCursor(Loc.GetString("sw-senate-budget-locked"), args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnOpened(EntityUid uid, SWSenateWorkConsoleComponent comp, BoundUIOpenedEvent args)
    {
        UpdateUi((uid, comp));
    }

    private void OnInserted(Entity<SWSenateWorkConsoleComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID == ent.Comp.IdSlot)
            UpdateUi(ent);
    }

    private void OnRemoved(Entity<SWSenateWorkConsoleComponent> ent, ref EntRemovedFromContainerMessage args)
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

        var query = EntityQueryEnumerator<SWSenateWorkConsoleComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (_ui.IsUiOpen(uid, SWSenateWorkConsoleUiKey.Key))
                UpdateUi((uid, comp));
        }
    }

    private void OnTakeOffice(Entity<SWSenateWorkConsoleComponent> ent, ref SWSenateTakeOfficeMessage args)
    {
        var card = _itemSlots.GetItemOrNull(ent.Owner, ent.Comp.IdSlot);
        if (card == null || !TryComp<SWSenatePositionCardComponent>(card, out var position))
        {
            _popup.PopupCursor(Loc.GetString("sw-senate-work-no-card"), args.Actor, PopupType.Medium);
            return;
        }

        switch (position.Position)
        {
            case SWSenatePosition.Senator:
                _popup.PopupCursor(Loc.GetString("sw-senate-work-senator-hint"), args.Actor, PopupType.Medium);
                break;

            case SWSenatePosition.Chancellor:
                if (_senate.TryTakeChancellor(Name(args.Actor)))
                {
                    _floor.SetChancellor(args.Actor);
                    _popup.PopupCursor(Loc.GetString("sw-senate-work-took-office"), args.Actor, PopupType.Medium);
                }
                else
                    _popup.PopupCursor(Loc.GetString("sw-senate-work-office-taken"), args.Actor, PopupType.Medium);
                break;

            case SWSenatePosition.Speaker:
                if (_senate.TryTakeSpeaker(Name(args.Actor)))
                {
                    _floor.SetSpeaker(args.Actor);
                    _popup.PopupCursor(Loc.GetString("sw-senate-work-took-office"), args.Actor, PopupType.Medium);
                }
                else
                    _popup.PopupCursor(Loc.GetString("sw-senate-work-office-taken"), args.Actor, PopupType.Medium);
                break;
        }

        UpdateUi(ent);
    }

    private void OnResign(Entity<SWSenateWorkConsoleComponent> ent, ref SWSenateResignMessage args)
    {
        var card = _itemSlots.GetItemOrNull(ent.Owner, ent.Comp.IdSlot);
        if (card == null || !TryComp<SWSenatePositionCardComponent>(card, out var position))
        {
            _popup.PopupCursor(Loc.GetString("sw-senate-work-no-card"), args.Actor, PopupType.Medium);
            return;
        }

        switch (position.Position)
        {
            case SWSenatePosition.Senator:
                if (TryComp<SWSenateSectorComponent>(card, out var sectorComp) && sectorComp.Sector != null)
                {
                    _senate.Unclaim(sectorComp.Sector);
                    sectorComp.Sector = null;
                    _idCard.TryChangeJobTitle(card.Value, Loc.GetString("sw-senate-senator-base"));
                    _popup.PopupCursor(Loc.GetString("sw-senate-work-resigned"), args.Actor, PopupType.Medium);
                }
                break;

            case SWSenatePosition.Chancellor:
                _senate.ResignChancellor();
                _floor.SetChancellor(null);
                _popup.PopupCursor(Loc.GetString("sw-senate-work-resigned"), args.Actor, PopupType.Medium);
                break;

            case SWSenatePosition.Speaker:
                _senate.ResignSpeaker();
                _floor.SetSpeaker(null);
                _floor.RevokeFloor();
                _popup.PopupCursor(Loc.GetString("sw-senate-work-resigned"), args.Actor, PopupType.Medium);
                break;
        }

        UpdateUi(ent);
    }

    private void UpdateUi(Entity<SWSenateWorkConsoleComponent> ent)
    {
        var state = new SWSenateWorkConsoleState();

        var card = _itemSlots.GetItemOrNull(ent.Owner, ent.Comp.IdSlot);
        state.HasCard = card != null;
        state.Globals = _senate.BuildGlobalEntries();
        state.Roster = BuildRoster();
        state.Bills = _senate.BuildBillEntries();
        state.Presets = _senate.BuildBillPresets();
        state.AllDilemmas = _dilemma.BuildAllDilemmas();
        var dilemmaOwner = GetDilemmaOwner(ent);
        state.MyDilemmas = _dilemma.BuildOwnerDilemmas(dilemmaOwner);
        state.MyBudgetDilemmas = _dilemma.BuildOwnerBudgetDilemmas(dilemmaOwner);
        state.MinistryNotices = _dilemma.GetNotices(dilemmaOwner);
        state.CanDilemmas = dilemmaOwner != null;
        state.BudgetLocked = _budget.Locked;
        state.BudgetTotal = _budget.Total();
        state.BudgetReserve = _budget.Reserve();
        state.MyBudgetAvailable = dilemmaOwner != null ? _budget.Available(dilemmaOwner) : 0;
        state.IsChancellorView = ChancellorInOffice(ent);
        if (state.IsChancellorView)
            state.BudgetSectors = BuildBudgetSectors();
        var programOwner = GetSectorOwner(ent);
        state.CanPrograms = programOwner != null;
        if (programOwner != null)
            state.Programs = _program.BuildPrograms(programOwner);
        state.CanAmbitions = dilemmaOwner != null;
        if (dilemmaOwner != null)
            state.Ambitions = _ambition.BuildAmbitions(dilemmaOwner);
        state.Crises = _crisis.BuildCrises(programOwner, ChancellorInOffice(ent));
        state.CanParties = programOwner != null;
        state.Parties = _party.BuildParties(programOwner);
        state.MyPartyId = _party.GetPartyIdOf(programOwner);
        state.PartyLaws = _party.BuildPartyLaws();
        state.DraftBudget = ent.Comp.DraftBudget;
        state.GameTime = _time.TimeString();
        state.Schedule = _senate.BuildScheduleRows();
        state.SchedulableBills = _senate.BuildSchedulableBills();
        state.FreeStartHours = _senate.BuildFreeStartHours();
        state.PendingSessions = _senate.BuildPendingSessions();
        state.CanScheduleSessions = CanScheduleAs(ent);
        state.CanConfirmSessions = CanConfirmAs(ent);
        state.OfficesVacant = _senate.OfficesVacant;
        state.WarProposed = _defense.WarPending;
        state.CanApproveWar = _defense.WarPending && CanConfirmAs(ent);
        state.PendingStanceName = _defense.PendingStance is { } pendingStance ? SWMinistryDefenseSystem.StanceName(pendingStance) : string.Empty;

        if (card != null && TryComp<SWSenatePositionCardComponent>(card, out var position))
        {
            state.IsSenateCard = true;
            state.Position = position.Position;

            switch (position.Position)
            {
                case SWSenatePosition.Senator:
                    string? sectorId = null;
                    if (TryComp<SWSenateSectorComponent>(card, out var sectorComp) && sectorComp.Sector != null)
                        sectorId = sectorComp.Sector;

                    if (sectorId != null && _proto.TryIndex<SWSectorPrototype>(sectorId, out var sectorProto))
                    {
                        state.PositionLabel = Loc.GetString("sw-senate-work-position-senator", ("sector", sectorProto.Name));
                        state.InOffice = true;
                        state.CanResign = true;
                        state.SectorName = sectorProto.Name;

                        if (_senate.SectorGlobals.ContainsKey(sectorId))
                        {
                            state.SectorStats = _senate.BuildSectorGlobals(sectorId);
                            var republic = _senate.GetRepublicGlobals();
                            foreach (var stat in state.SectorStats)
                            {
                                var target = republic.TryGetValue(stat.Id, out var r) ? r : stat.Value;
                                state.SectorTrends.Add(Math.Sign(target - stat.Value));
                            }
                        }
                    }
                    else
                    {
                        state.PositionLabel = Loc.GetString("sw-senate-work-position-senator-none");
                    }
                    break;

                case SWSenatePosition.Chancellor:
                    state.PositionLabel = Loc.GetString("sw-senate-work-position-chancellor");
                    state.InOffice = _senate.Chancellor != null;
                    state.CanTake = _senate.Chancellor == null;
                    state.CanResign = _senate.Chancellor != null;
                    break;

                case SWSenatePosition.Speaker:
                    state.PositionLabel = Loc.GetString("sw-senate-work-position-speaker");
                    state.InOffice = _senate.Speaker != null;
                    state.CanTake = _senate.Speaker == null;
                    state.CanResign = _senate.Speaker != null;
                    break;
            }
        }

        _ui.SetUiState(ent.Owner, SWSenateWorkConsoleUiKey.Key, state);
    }

    private List<SWBudgetSectorEntry> BuildBudgetSectors()
    {
        var list = new List<SWBudgetSectorEntry>();
        foreach (var proto in _proto.EnumeratePrototypes<SWSectorPrototype>())
        {
            if (!_senate.IsClaimed(proto.ID))
                continue;

            list.Add(new SWBudgetSectorEntry
            {
                SectorId = proto.ID,
                Name = proto.Name,
                Color = proto.Color,
                Amount = _budget.Allocation(proto.ID),
            });
        }

        list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
        return list;
    }

    private List<SWSenateRosterEntry> BuildRoster()
    {
        var roster = new List<SWSenateRosterEntry>();

        if (_senate.Chancellor != null)
            roster.Add(new SWSenateRosterEntry { Position = Loc.GetString("sw-senate-work-position-chancellor"), Holder = _senate.Chancellor, Color = Color.Gold });

        if (_senate.Speaker != null)
            roster.Add(new SWSenateRosterEntry { Position = Loc.GetString("sw-senate-work-position-speaker"), Holder = _senate.Speaker, Color = Color.SkyBlue });

        foreach (var proto in _proto.EnumeratePrototypes<SWSectorPrototype>())
        {
            var holder = _senate.GetSectorHolder(proto.ID);
            if (holder == null)
                continue;

            roster.Add(new SWSenateRosterEntry { Position = proto.Name, Holder = holder, Color = proto.Color });
        }

        return roster;
    }

}
