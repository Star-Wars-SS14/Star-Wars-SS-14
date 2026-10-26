using Content.Server.Popups;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Sectors;
using Content.Shared.Access.Systems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Content.Server._SW._Senate.Core;

namespace Content.Server._SW._Senate.Consoles;

public sealed class SWSectorSelectConsoleSystem : EntitySystem
{
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly SWSenateStateSystem _senate = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly SharedIdCardSystem _idCard = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<SWSectorSelectConsoleComponent, BoundUIOpenedEvent>(OnOpened);
        SubscribeLocalEvent<SWSectorSelectConsoleComponent, EntInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<SWSectorSelectConsoleComponent, EntRemovedFromContainerMessage>(OnRemoved);
        SubscribeLocalEvent<SWSectorSelectConsoleComponent, SWSelectSectorMessage>(OnSelect);
    }

    private void OnOpened(EntityUid uid, SWSectorSelectConsoleComponent comp, BoundUIOpenedEvent args)
    {
        UpdateUi((uid, comp));
    }

    private void OnInserted(Entity<SWSectorSelectConsoleComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (!_time.ModeActive || !_time.DayActive)
            return;

        if (args.Container.ID == ent.Comp.IdSlot)
            UpdateUi(ent);
    }

    private void OnRemoved(Entity<SWSectorSelectConsoleComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (!_time.ModeActive || !_time.DayActive)
            return;

        if (args.Container.ID == ent.Comp.IdSlot)
            UpdateUi(ent);
    }

    private void OnSelect(Entity<SWSectorSelectConsoleComponent> ent, ref SWSelectSectorMessage args)
    {
        if (!_time.ModeActive || !_time.DayActive)
            return;

        var card = _itemSlots.GetItemOrNull(ent.Owner, ent.Comp.IdSlot);
        if (card == null)
        {
            _popup.PopupCursor(Loc.GetString("sw-senate-select-no-card"), args.Actor, PopupType.Medium);
            return;
        }

        if (!TryComp<SWSenatePositionCardComponent>(card, out var position) || position.Position != SWSenatePosition.Senator)
        {
            _popup.PopupCursor(Loc.GetString("sw-senate-select-not-senator"), args.Actor, PopupType.Medium);
            return;
        }

        if (TryComp<SWSenateSectorComponent>(card, out var existing) && existing.Sector != null)
        {
            _popup.PopupCursor(Loc.GetString("sw-senate-select-already-assigned"), args.Actor, PopupType.Medium);
            return;
        }

        if (!_proto.TryIndex<SWSectorPrototype>(args.SectorId, out var sectorProto))
            return;

        if (!_senate.TryClaim(args.SectorId, Name(args.Actor)))
        {
            _popup.PopupCursor(Loc.GetString("sw-senate-select-taken"), args.Actor, PopupType.Medium);
            UpdateUi(ent);
            return;
        }

        var sectorComp = EnsureComp<SWSenateSectorComponent>(card.Value);
        sectorComp.Sector = args.SectorId;

        _idCard.TryChangeJobTitle(card.Value, Loc.GetString("sw-senate-senator-title", ("sector", sectorProto.Name)));

        _popup.PopupCursor(Loc.GetString("sw-senate-select-success", ("sector", sectorProto.Name)), args.Actor, PopupType.Medium);
        UpdateUi(ent);
    }

    private void UpdateUi(Entity<SWSectorSelectConsoleComponent> ent)
    {
        var card = _itemSlots.GetItemOrNull(ent.Owner, ent.Comp.IdSlot);

        var isSenatorCard = card != null
            && TryComp<SWSenatePositionCardComponent>(card, out var position)
            && position.Position == SWSenatePosition.Senator;

        string? currentSector = null;
        string? currentSectorName = null;
        if (card != null && TryComp<SWSenateSectorComponent>(card, out var sectorComp) && sectorComp.Sector != null)
        {
            currentSector = sectorComp.Sector;
            if (_proto.TryIndex<SWSectorPrototype>(currentSector, out var proto))
                currentSectorName = proto.Name;
        }

        var sectors = new List<SWSectorEntry>();
        foreach (var proto in _proto.EnumeratePrototypes<SWSectorPrototype>())
        {
            sectors.Add(new SWSectorEntry
            {
                Id = proto.ID,
                Name = proto.Name,
                Color = proto.Color,
                Claimed = _senate.IsClaimed(proto.ID),
            });
        }

        sectors.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));

        _ui.SetUiState(ent.Owner, SWSectorSelectConsoleUiKey.Key,
            new SWSectorSelectConsoleState(sectors, card != null, isSenatorCard, currentSector, currentSectorName));
    }
}
