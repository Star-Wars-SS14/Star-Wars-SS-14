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

namespace Content.Server._SW._Senate;

public sealed class SWSenateWorkConsoleSystem : EntitySystem
{
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly SWSenateStateSystem _senate = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly SharedIdCardSystem _idCard = default!;

    private const float UpdateInterval = 3f;
    private float _accumulator;

    public override void Initialize()
    {
        SubscribeLocalEvent<SWSenateWorkConsoleComponent, BoundUIOpenedEvent>(OnOpened);
        SubscribeLocalEvent<SWSenateWorkConsoleComponent, EntInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<SWSenateWorkConsoleComponent, EntRemovedFromContainerMessage>(OnRemoved);
        SubscribeLocalEvent<SWSenateWorkConsoleComponent, SWSenateTakeOfficeMessage>(OnTakeOffice);
        SubscribeLocalEvent<SWSenateWorkConsoleComponent, SWSenateResignMessage>(OnResign);
        SubscribeLocalEvent<SWSenateWorkConsoleComponent, SWSenateSubmitBillMessage>(OnSubmitBill);
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

        var name = args.Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            _popup.PopupCursor(Loc.GetString("sw-senate-bill-no-name"), args.Actor, PopupType.Medium);
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
                    _popup.PopupCursor(Loc.GetString("sw-senate-work-took-office"), args.Actor, PopupType.Medium);
                else
                    _popup.PopupCursor(Loc.GetString("sw-senate-work-office-taken"), args.Actor, PopupType.Medium);
                break;

            case SWSenatePosition.Speaker:
                if (_senate.TryTakeSpeaker(Name(args.Actor)))
                    _popup.PopupCursor(Loc.GetString("sw-senate-work-took-office"), args.Actor, PopupType.Medium);
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
                _popup.PopupCursor(Loc.GetString("sw-senate-work-resigned"), args.Actor, PopupType.Medium);
                break;

            case SWSenatePosition.Speaker:
                _senate.ResignSpeaker();
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
        state.RepublicStats = BuildStatEntries(_senate.GetRepublicStats());
        state.Roster = BuildRoster();
        state.Bills = BuildBills();
        state.DraftBudget = ent.Comp.DraftBudget;

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

                        if (_senate.SectorStats.TryGetValue(sectorId, out var stats))
                        {
                            state.SectorStats = BuildStatEntries(stats);
                            var republic = _senate.GetRepublicStats();
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

    private List<SWBillEntry> BuildBills()
    {
        var bills = new List<SWBillEntry>();
        foreach (var bill in _senate.Bills)
        {
            var effects = new List<SWBillEffectEntry>();
            foreach (var proto in _proto.EnumeratePrototypes<SWStatPrototype>().OrderBy(s => s.Order))
            {
                if (!bill.Effects.TryGetValue(proto.ID, out var delta))
                    continue;

                effects.Add(new SWBillEffectEntry { StatName = proto.ShortName, Color = proto.Color, Delta = delta });
            }

            bills.Add(new SWBillEntry
            {
                Id = bill.Id,
                Name = bill.Name,
                Author = bill.Author,
                Stage = Loc.GetString(StageLoc(bill.Stage)),
                Effects = effects,
            });
        }

        return bills;
    }

    private static string StageLoc(SWBillStage stage) => stage switch
    {
        SWBillStage.Voting => "sw-senate-bill-stage-voting",
        SWBillStage.Passed => "sw-senate-bill-stage-passed",
        SWBillStage.Rejected => "sw-senate-bill-stage-rejected",
        _ => "sw-senate-bill-stage-voting",
    };

    private List<SWStatEntry> BuildStatEntries(Dictionary<string, float> stats)
    {
        var list = new List<SWStatEntry>();
        foreach (var proto in _proto.EnumeratePrototypes<SWStatPrototype>().OrderBy(s => s.Order))
        {
            if (!stats.TryGetValue(proto.ID, out var value))
                continue;

            list.Add(new SWStatEntry
            {
                Id = proto.ID,
                Name = proto.Name,
                ShortName = proto.ShortName,
                Color = proto.Color,
                Value = value,
                Min = proto.Min,
                Max = proto.Max,
            });
        }

        return list;
    }
}
