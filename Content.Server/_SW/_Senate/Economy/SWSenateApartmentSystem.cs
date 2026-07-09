using System.Linq;
using Content.Server._SW._Senate.Core;
using Content.Server.Access.Systems;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Apartments;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.GameTicking;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server._SW._Senate.Economy;

// A door placed by a mapper: its own AccessReader access list holds one slot tag (drawn from a shared
// pool of generic accessLevel prototypes), and ClassId says which SWApartmentClassPrototype it belongs
// to. The slot tag doubles as this apartment's unique identity - nothing else needs a separate id.
[RegisterComponent]
public sealed partial class SWApartmentDoorComponent : Component
{
    [DataField(required: true)]
    public ProtoId<SWApartmentClassPrototype> ClassId;
}

[RegisterComponent]
public sealed partial class SWApartmentConsoleComponent : Component
{
}

public sealed class SWSenateApartmentSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly SWCorruptionSystem _corruption = default!;
    [Dependency] private readonly SWMinistryFinanceSystem _finance = default!;
    [Dependency] private readonly SharedIdCardSystem _idCard = default!;
    [Dependency] private readonly AccessSystem _access = default!;

    private sealed class Lease
    {
        public string Tenant = string.Empty;
        public string ClassId = string.Empty;
    }

    private readonly Dictionary<string, Lease> _leases = new();
    private int _day = -1;

    public override void Initialize()
    {
        SubscribeLocalEvent<SWApartmentConsoleComponent, BeforeActivatableUIOpenEvent>(OnOpen);
        SubscribeLocalEvent<SWApartmentConsoleComponent, SWApartmentRentMessage>(OnRent);
        SubscribeLocalEvent<SWApartmentConsoleComponent, SWApartmentVacateMessage>(OnVacate);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _leases.Clear();
        _day = -1;
    }

    public override void Update(float frameTime)
    {
        if (!_time.ModeActive)
            return;

        if (_time.Day == _day)
            return;

        _day = _time.Day;
        ChargeAll();
    }

    // Billed once per game day: renting pays for the first day up front, then every day boundary tries
    // to collect the next payment. Can't pay -> evicted immediately, slot goes back on the market.
    private void ChargeAll()
    {
        foreach (var (slotTag, lease) in _leases.ToList())
        {
            if (!_proto.TryIndex<SWApartmentClassPrototype>(lease.ClassId, out var cls))
            {
                _leases.Remove(slotTag);
                continue;
            }

            if (_corruption.SpendAccount(lease.Tenant, AdjustedPrice(cls.PricePerDay)))
                continue;

            _leases.Remove(slotTag);
            RevokeAccess(slotTag);
        }
    }

    private void OnOpen(Entity<SWApartmentConsoleComponent> ent, ref BeforeActivatableUIOpenEvent args)
    {
        UpdateUi(ent, args.User, string.Empty);
    }

    private void OnRent(Entity<SWApartmentConsoleComponent> ent, ref SWApartmentRentMessage args)
    {
        if (!_time.ModeActive)
            return;

        var slotTag = args.SlotTag;
        var actor = args.Actor;

        var slot = BuildSlotPool().FirstOrDefault(s => s.SlotTag == slotTag);
        if (slot.SlotTag == null || _leases.ContainsKey(slotTag))
        {
            UpdateUi(ent, actor, "Апартаменты уже заняты.");
            return;
        }

        if (!_proto.TryIndex<SWApartmentClassPrototype>(slot.ClassId, out var cls))
            return;

        var tenant = Name(actor);
        var price = AdjustedPrice(cls.PricePerDay);

        if (!_corruption.SpendAccount(tenant, price))
        {
            UpdateUi(ent, actor, $"Недостаточно средств: нужно {price} кр.");
            return;
        }

        VacateCurrent(tenant);

        _leases[slotTag] = new Lease { Tenant = tenant, ClassId = slot.ClassId };
        GrantAccess(actor, slotTag);

        UpdateUi(ent, actor, $"Апартаменты класса «{cls.Name}» арендованы.");
    }

    private void OnVacate(Entity<SWApartmentConsoleComponent> ent, ref SWApartmentVacateMessage args)
    {
        var tenant = Name(args.Actor);
        var notice = VacateCurrent(tenant) ? "Апартаменты освобождены." : string.Empty;
        UpdateUi(ent, args.Actor, notice);
    }

    private bool VacateCurrent(string tenant)
    {
        var existing = _leases.FirstOrDefault(kv => kv.Value.Tenant == tenant);
        if (existing.Key == null)
            return false;

        _leases.Remove(existing.Key);
        RevokeAccess(existing.Key);
        return true;
    }

    private void GrantAccess(EntityUid actor, string slotTag)
    {
        if (!_idCard.TryFindIdCard(actor, out var idCard))
            return;

        var tags = _access.TryGetTags(idCard) is { } current
            ? new List<ProtoId<AccessLevelPrototype>>(current)
            : new List<ProtoId<AccessLevelPrototype>>();

        if (!tags.Contains(slotTag))
            tags.Add(slotTag);

        _access.TrySetTags(idCard, tags);
    }

    // Eviction/vacating only know the tenant's NAME (billing runs with nobody present), not their
    // current entity, so instead of hunting for "their" card we strip the slot tag from whichever card
    // currently carries it - correct regardless of identity, and self-heals any stale grant.
    private void RevokeAccess(string slotTag)
    {
        var query = EntityQueryEnumerator<AccessComponent>();
        while (query.MoveNext(out var uid, out var access))
        {
            if (!access.Tags.Contains(slotTag))
                continue;

            var tags = new List<ProtoId<AccessLevelPrototype>>(access.Tags);
            tags.Remove(slotTag);
            _access.TrySetTags(uid, tags);
        }
    }

    private List<(string SlotTag, string ClassId)> BuildSlotPool()
    {
        var list = new List<(string, string)>();
        var query = EntityQueryEnumerator<SWApartmentDoorComponent, AccessReaderComponent>();
        while (query.MoveNext(out _, out var door, out var reader))
        {
            if (reader.AccessLists.Count == 0 || reader.AccessLists[0].Count == 0)
                continue;

            list.Add((reader.AccessLists[0].First().Id, door.ClassId.Id));
        }

        return list;
    }

    private int AdjustedPrice(int price) =>
        Math.Max(10, (int) MathF.Round(price * _finance.PriceMultiplier() / 10f) * 10);

    private void UpdateUi(EntityUid uid, EntityUid user, string notice)
    {
        var name = Name(user);
        var mySlot = _leases.FirstOrDefault(kv => kv.Value.Tenant == name).Key;

        var state = new SWApartmentConsoleState
        {
            UserName = name,
            Balance = _corruption.Account(name),
            MySlotTag = mySlot,
            Notice = notice,
        };

        var classes = _proto.EnumeratePrototypes<SWApartmentClassPrototype>()
            .OrderBy(c => c.Order)
            .ToDictionary(c => c.ID);

        foreach (var (slotTag, classId) in BuildSlotPool().OrderBy(s => s.ClassId).ThenBy(s => s.SlotTag))
        {
            if (!classes.TryGetValue(classId, out var cls))
                continue;

            var occupied = _leases.TryGetValue(slotTag, out var lease);
            state.Slots.Add(new SWApartmentSlotEntry
            {
                SlotTag = slotTag,
                ClassId = classId,
                ClassName = cls.Name,
                ClassColor = cls.Color,
                Price = AdjustedPrice(cls.PricePerDay),
                Occupied = occupied,
                TenantName = occupied ? lease!.Tenant : string.Empty,
                IsMine = occupied && lease!.Tenant == name,
            });
        }

        _ui.SetUiState(uid, SWApartmentConsoleUiKey.Key, state);
    }
}
