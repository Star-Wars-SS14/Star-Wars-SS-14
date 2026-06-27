using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Ministries;
using Content.Shared.Containers.ItemSlots;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Content.Server._SW._Senate.Core;

namespace Content.Server._SW._Senate.Consoles;

public sealed class SWMinistryWorkConsoleSystem : EntitySystem
{
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly SWSenateStateSystem _senate = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;

    private const float UpdateInterval = 3f;
    private float _accumulator;

    public override void Initialize()
    {
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, BoundUIOpenedEvent>(OnOpened);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, EntInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<SWMinistryWorkConsoleComponent, EntRemovedFromContainerMessage>(OnRemoved);
    }

    private void OnOpened(EntityUid uid, SWMinistryWorkConsoleComponent comp, BoundUIOpenedEvent args)
    {
        UpdateUi((uid, comp));
    }

    private void OnInserted(Entity<SWMinistryWorkConsoleComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID == ent.Comp.IdSlot)
            UpdateUi(ent);
    }

    private void OnRemoved(Entity<SWMinistryWorkConsoleComponent> ent, ref EntRemovedFromContainerMessage args)
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

        var query = EntityQueryEnumerator<SWMinistryWorkConsoleComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (_ui.IsUiOpen(uid, SWMinistryWorkConsoleUiKey.Key))
                UpdateUi((uid, comp));
        }
    }

    private void UpdateUi(Entity<SWMinistryWorkConsoleComponent> ent)
    {
        var card = _itemSlots.GetItemOrNull(ent.Owner, ent.Comp.IdSlot);

        var state = new SWMinistryWorkConsoleState
        {
            HasCard = card != null,
            GameTime = _time.TimeString(),
            Globals = _senate.BuildGlobalEntries(),
        };

        if (card != null
            && TryComp<SWSenatePositionCardComponent>(card, out var position)
            && position.Position == SWSenatePosition.Minister)
        {
            state.IsMinisterCard = true;

            if (TryComp<SWMinistryComponent>(card, out var ministryComp)
                && ministryComp.Ministry != null
                && _proto.TryIndex<SWMinistryPrototype>(ministryComp.Ministry, out var proto))
            {
                state.HasMinistry = true;
                state.MinistryName = proto.Name;
                state.MinistryTitle = Loc.GetString(TitleKey(proto.Kind), ("name", proto.TitleName));
                state.MinistryDescription = proto.Description;
                state.MinistryColor = proto.Color;
                state.KindLabel = KindLabel(proto.Kind);
                state.DomainStatId = proto.Domain;
            }
        }

        _ui.SetUiState(ent.Owner, SWMinistryWorkConsoleUiKey.Key, state);
    }

    private static string TitleKey(SWMinistryKind kind) => kind switch
    {
        SWMinistryKind.Department => "sw-ministry-title-department",
        SWMinistryKind.Bureau => "sw-ministry-title-bureau",
        _ => "sw-ministry-title-ministry",
    };

    private static string KindLabel(SWMinistryKind kind) => kind switch
    {
        SWMinistryKind.Department => "Департамент",
        SWMinistryKind.Bureau => "Бюро",
        _ => "Министерство",
    };
}
