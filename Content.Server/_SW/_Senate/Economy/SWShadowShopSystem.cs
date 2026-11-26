using Content.Server._SW._Senate.Core;
using Content.Shared._SW._Senate;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server._SW._Senate.Economy;

[DataDefinition]
public sealed partial class SWShadowShopItem
{
    [DataField(required: true)]
    public EntProtoId Proto;

    [DataField(required: true)]
    public int Price;
}

[RegisterComponent]
public sealed partial class SWShadowShopComponent : Component
{
    [DataField(required: true)]
    public List<SWShadowShopItem> Catalog = new();
}

public sealed class SWShadowShopSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly SWCorruptionSystem _corruption = default!;
    [Dependency] private readonly SWMinistryFinanceSystem _finance = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<SWShadowShopComponent, BeforeActivatableUIOpenEvent>(OnOpen);
        SubscribeLocalEvent<SWShadowShopComponent, SWShadowShopBuyMessage>(OnBuy);
    }

    private void OnOpen(EntityUid uid, SWShadowShopComponent comp, BeforeActivatableUIOpenEvent args)
    {
        UpdateUi(uid, comp, args.User, string.Empty);
    }

    private void OnBuy(EntityUid uid, SWShadowShopComponent comp, SWShadowShopBuyMessage args)
    {
        if (!_time.ModeActive)
            return;

        if (args.Index < 0 || args.Index >= comp.Catalog.Count)
            return;

        var item = comp.Catalog[args.Index];
        var price = AdjustedPrice(item.Price);
        var buyer = Name(args.Actor);

        string notice;
        if (_corruption.SpendAccount(buyer, price))
        {
            Spawn(item.Proto, Transform(uid).Coordinates);
            notice = "Покупка оплачена. Заберите товар из лотка.";
        }
        else
        {
            notice = $"Недостаточно средств: нужно {price} кр.";
        }

        UpdateUi(uid, comp, args.Actor, notice);
    }

    private int AdjustedPrice(int price) =>
        Math.Max(10, (int) MathF.Round(price * _finance.PriceMultiplier() / 10f) * 10);

    private void UpdateUi(EntityUid uid, SWShadowShopComponent comp, EntityUid user, string notice)
    {
        var name = Name(user);
        var state = new SWShadowShopState
        {
            ShopName = Name(uid),
            UserName = name,
            Balance = _corruption.Account(name),
            Notice = notice,
        };

        for (var i = 0; i < comp.Catalog.Count; i++)
        {
            var item = comp.Catalog[i];
            var label = _proto.TryIndex<EntityPrototype>(item.Proto, out var proto) ? proto.Name : item.Proto.Id;
            state.Items.Add(new SWShadowShopEntry
            {
                Index = i,
                Name = label,
                Price = AdjustedPrice(item.Price),
            });
        }

        _ui.SetUiState(uid, SWShadowShopUiKey.Key, state);
    }
}
