using Content.Server.Popups;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Ministries;
using Content.Shared.Access.Systems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Content.Server._SW._Senate.Core;

namespace Content.Server._SW._Senate.Consoles;

public sealed class SWMinistrySelectConsoleSystem : EntitySystem
{
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly SWMinistrySystem _ministry = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly SharedIdCardSystem _idCard = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<SWMinistrySelectConsoleComponent, BoundUIOpenedEvent>(OnOpened);
        SubscribeLocalEvent<SWMinistrySelectConsoleComponent, EntInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<SWMinistrySelectConsoleComponent, EntRemovedFromContainerMessage>(OnRemoved);
        SubscribeLocalEvent<SWMinistrySelectConsoleComponent, SWSelectMinistryMessage>(OnSelect);
    }

    private void OnOpened(EntityUid uid, SWMinistrySelectConsoleComponent comp, BoundUIOpenedEvent args)
    {
        UpdateUi((uid, comp));
    }

    private void OnInserted(Entity<SWMinistrySelectConsoleComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (!_time.ModeActive || !_time.DayActive)
            return;

        if (args.Container.ID == ent.Comp.IdSlot)
            UpdateUi(ent);
    }

    private void OnRemoved(Entity<SWMinistrySelectConsoleComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (!_time.ModeActive || !_time.DayActive)
            return;

        if (args.Container.ID == ent.Comp.IdSlot)
            UpdateUi(ent);
    }

    private void OnSelect(Entity<SWMinistrySelectConsoleComponent> ent, ref SWSelectMinistryMessage args)
    {
        if (!_time.ModeActive || !_time.DayActive)
            return;

        var card = _itemSlots.GetItemOrNull(ent.Owner, ent.Comp.IdSlot);
        if (card == null)
        {
            _popup.PopupCursor(Loc.GetString("sw-senate-select-no-card"), args.Actor, PopupType.Medium);
            return;
        }

        if (!TryComp<SWSenatePositionCardComponent>(card, out var position) || position.Position != SWSenatePosition.Minister)
        {
            _popup.PopupCursor(Loc.GetString("sw-ministry-select-not-minister"), args.Actor, PopupType.Medium);
            return;
        }

        _ministry.ReconcileCard(card.Value);

        if (TryComp<SWMinistryComponent>(card, out var existing) && existing.Ministry != null)
        {
            _popup.PopupCursor(Loc.GetString("sw-ministry-select-already"), args.Actor, PopupType.Medium);
            return;
        }

        if (!_proto.TryIndex<SWMinistryPrototype>(args.MinistryId, out var proto))
            return;

        if (proto.Kind == SWMinistryKind.Department && proto.Parent is { } parentId && !_ministry.IsClaimed(parentId))
        {
            var parentName = _proto.TryIndex<SWMinistryPrototype>(parentId, out var pp) ? pp.Name : parentId.Id;
            _popup.PopupCursor(Loc.GetString("sw-ministry-select-requires-active", ("name", parentName)), args.Actor, PopupType.Medium);
            return;
        }

        if (proto.RequiresActive is { } required && !_ministry.IsClaimed(required))
        {
            var reqName = _proto.TryIndex<SWMinistryPrototype>(required, out var reqProto) ? reqProto.Name : required.Id;
            _popup.PopupCursor(Loc.GetString("sw-ministry-select-requires-active", ("name", reqName)), args.Actor, PopupType.Medium);
            return;
        }

        if (_ministry.IsBarred(args.MinistryId, Name(args.Actor)))
        {
            _popup.PopupCursor(Loc.GetString("sw-ministry-select-barred"), args.Actor, PopupType.Medium);
            return;
        }

        if (!_ministry.TryClaim(args.MinistryId, Name(args.Actor), card.Value, args.Actor))
        {
            _popup.PopupCursor(Loc.GetString("sw-ministry-select-taken"), args.Actor, PopupType.Medium);
            UpdateUi(ent);
            return;
        }

        var comp = EnsureComp<SWMinistryComponent>(card.Value);
        comp.Ministry = args.MinistryId;

        var title = Loc.GetString(TitleKey(proto.Kind), ("name", proto.TitleName));
        _idCard.TryChangeJobTitle(card.Value, title);

        _popup.PopupCursor(Loc.GetString("sw-ministry-select-success", ("title", title)), args.Actor, PopupType.Medium);
        UpdateUi(ent);
    }

    private void UpdateUi(Entity<SWMinistrySelectConsoleComponent> ent)
    {
        var card = _itemSlots.GetItemOrNull(ent.Owner, ent.Comp.IdSlot);

        if (card != null)
            _ministry.ReconcileCard(card.Value);

        var isMinisterCard = card != null
            && TryComp<SWSenatePositionCardComponent>(card, out var position)
            && position.Position == SWSenatePosition.Minister;

        string? current = null;
        string? currentName = null;
        if (card != null && TryComp<SWMinistryComponent>(card, out var ministryComp) && ministryComp.Ministry != null)
        {
            current = ministryComp.Ministry;
            if (_proto.TryIndex<SWMinistryPrototype>(current, out var proto))
                currentName = proto.Name;
        }

        var ministries = new List<SWMinistryEntry>();
        foreach (var proto in _proto.EnumeratePrototypes<SWMinistryPrototype>())
        {
            string? parentName = null;
            if (proto.Parent != null && _proto.TryIndex<SWMinistryPrototype>(proto.Parent, out var parent))
                parentName = parent.Name;

            var lockedReason = string.Empty;
            if (proto.Kind == SWMinistryKind.Department && proto.Parent is { } depParent && !_ministry.IsClaimed(depParent))
                lockedReason = $"Должно быть занято: {parentName ?? depParent.Id}";
            else if (proto.RequiresActive is { } req && !_ministry.IsClaimed(req))
                lockedReason = _proto.TryIndex<SWMinistryPrototype>(req, out var reqProto)
                    ? $"Должно быть занято: {reqProto.Name}"
                    : $"Должно быть занято: {req.Id}";

            ministries.Add(new SWMinistryEntry
            {
                Id = proto.ID,
                Name = proto.Name,
                Description = proto.Description,
                Color = proto.Color,
                KindGroup = (int) proto.Kind,
                KindLabel = KindLabel(proto.Kind),
                ParentName = parentName,
                Claimed = _ministry.IsClaimed(proto.ID),
                Holder = _ministry.GetHolder(proto.ID),
                Locked = lockedReason.Length > 0,
                LockedReason = lockedReason,
            });
        }

        ministries.Sort((a, b) =>
        {
            var byKind = a.KindGroup.CompareTo(b.KindGroup);
            return byKind != 0 ? byKind : string.Compare(a.Name, b.Name, StringComparison.Ordinal);
        });

        _ui.SetUiState(ent.Owner, SWMinistrySelectConsoleUiKey.Key,
            new SWMinistrySelectConsoleState(ministries, card != null, isMinisterCard, current, currentName));
    }

    private static string TitleKey(SWMinistryKind kind) => kind switch
    {
        SWMinistryKind.Department => "sw-ministry-title-department",
        SWMinistryKind.Bureau => "sw-ministry-title-bureau",
        _ => "sw-ministry-title-ministry",
    };

    private static string KindLabel(SWMinistryKind kind) => kind switch
    {
        SWMinistryKind.Department => "ДЕПАРТАМЕНТЫ",
        SWMinistryKind.Bureau => "БЮРО",
        _ => "МИНИСТЕРСТВА",
    };
}
