using Content.Server._SW.FinanceConsole;
using Content.Server._SW.TurretConsole;
using Content.Server.Cargo.Components;
using Content.Shared._SW.Economy.BankSystem;
using Content.Shared._SW.Economy.Messages;
using Content.Shared._SW.Economy.Prototypes;
using Content.Shared.Cargo.Prototypes;
using Content.Shared.Coordinates;
using Content.Shared.DeviceLinking;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Toolshed.Commands.Math;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.Economy.BuyConsole;

public sealed class SWBuyConsoleSystem : EntitySystem
{
    [Dependency] SWBasePayableConsoleSystem _swPayable = default!;
    [Dependency] SWSharedBankSystem _sWSharedBankSystem = default!;
    [Dependency] private readonly UserInterfaceSystem _uiSystem = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly SharedTransformSystem _sharedTransform = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWBuyConsoleComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<SWBuyConsoleComponent, SWTryPurchaseMessage>(OnTryPurchase);
    }

    private void OnMapInit(Entity<SWBuyConsoleComponent> ent, ref MapInitEvent args)
    {
        var comp = EnsureComp<SWBankAccountContainerComponent>(ent);
        foreach (var id in ent.Comp.AllowedCurrencies)
        {
            comp.CurrentAmount[id] = 0f;
        }
        var markets = ent.Comp.AllowedMarkets;
        var products = ent.Comp.ExtraProducts.ToHashSet();
        foreach (var product in _prototypeManager.EnumeratePrototypes<SWCargoProductPrototype>())
        {
            if (!markets.Contains(product.Group))
                continue;

            products.Add(product.ID);
        }
        ent.Comp.AvailableProducts = products;
        UpdateInterface(ent);
    }

    public List<EntityUid> GetPallets(Entity<SWBuyConsoleComponent> ent)
    {
        var comp = Comp<DeviceLinkSourceComponent>(ent);

        var outputs = comp.Outputs;

        if (outputs.TryGetValue(ent.Comp.Port, out var pallets))
        {
            return pallets.ToList();
        }

        return new List<EntityUid>();
    }
    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<SWBuyConsoleComponent, SWBankAccountAccessorComponent>();
        while (query.MoveNext(out var uid, out var buyComp, out var comp))
        {
            if (comp.AccountInterfaceRequiresUpdate && comp.Account != null)
            {
                UpdateInterface(new Entity<SWBuyConsoleComponent>(uid, buyComp));
                comp.AccountInterfaceRequiresUpdate = false;
            }
        }
    }
    private void UpdateInterface(Entity<SWBuyConsoleComponent> ent)
    {
        var state = new SWBuyConsoleBounderUserInterfaceState();
        state.CurrentAmount = _swPayable.GetBalance(ent.Owner);
        state.Products = ent.Comp.AvailableProducts.ToList();
        state.Currencies = ent.Comp.AllowedCurrencies.ToList();
        _uiSystem.SetUiState(ent.Owner, SWBuyConsoleUIKey.Key, state);
    }

    private void OnTryPurchase(Entity<SWBuyConsoleComponent> ent, ref SWTryPurchaseMessage args)
    {
        if (!TryComp<SWBankAccountAccessorComponent>(ent, out var accComp))
        {
            return;
        }
        if (accComp.Account == null)
        {
            return;
        }
        var account = accComp.Account.Value;
        var proto = _prototypeManager.Index(args.CargoProductId);
        Dictionary<ProtoId<SWCurrencyPrototype>, float> totalCost = new Dictionary<ProtoId<SWCurrencyPrototype>, float>();
        foreach (var (currency, cost) in proto.Cost)
        {
            totalCost[currency] = cost * args.Amount;
        }
        _sWSharedBankSystem.FillCurrencies(ent.Comp.AllowedCurrencies, ref totalCost);
        if (_sWSharedBankSystem.TryPayAny(totalCost, account.Comp.CurrentAmount))
        {
            SpawnEntities(ent, proto.Product, args.Amount);
        }
        UpdateInterface(ent);
    }

    private void SpawnEntities(Entity<SWBuyConsoleComponent> ent, string proto, int amount)
    {
        var pallets = GetPallets(ent);
        var size = pallets.Count;
        if (size != 0)
        {
            for (int i = 0; i < amount; i++)
            {
                var coords = _sharedTransform.ToMapCoordinates(pallets[i % size].ToCoordinates());
                Spawn(proto, coords);
            }
        }
        else
        {
            for (int _ = 0; _ < amount; _++)
            {
                var coords = _sharedTransform.ToMapCoordinates(ent.Owner.ToCoordinates());
                Spawn(proto, coords);
            }
        }
    }
}
