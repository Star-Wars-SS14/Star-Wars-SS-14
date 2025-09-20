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
using JetBrains.FormatRipper.Elf;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
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
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWBuyConsoleComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<SWBuyConsoleComponent, SWTryPurchaseMessage>(OnTryPurchase);
        SubscribeLocalEvent<CargoPalletComponent, NewLinkEvent>(OnNewLink);
        SubscribeLocalEvent<CargoPalletComponent, PortDisconnectedEvent>(OnPortDisconnected);
        SubscribeLocalEvent<CargoPalletComponent, MapInitEvent>(OnPalletMapinit);
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
            if (ent.Comp.ConnectedPallets.Count > 0)
            {
                SpawnEntities(ent, proto.Product, args.Amount);
            }
            else
            {
                for (int _ = 0; _ < args.Amount; _++)
                {
                    Spawn(proto.Product, ent.Owner.ToCoordinates());
                }
            }
        }

        UpdateInterface(ent);
    }

    private void OnNewLink(Entity<CargoPalletComponent> ent, ref NewLinkEvent args)
    {
        UpdateConnectedPallets(ent, true);
    }
    private void OnPortDisconnected(Entity<CargoPalletComponent> ent, ref PortDisconnectedEvent args)
    {
        UpdateConnectedPallets(ent, false);
    }

    private void OnPalletMapinit(Entity<CargoPalletComponent> ent, ref MapInitEvent args)
    {
        UpdateConnectedPallets(ent, true);
    }
    private void UpdateConnectedPallets(Entity<CargoPalletComponent> ent, bool add)
    {
        if (!TryComp<DeviceLinkSinkComponent>(ent.Owner, out var comp))
        {
            return;
        }

        foreach (var source in comp.LinkedSources)
        {
            if (TryComp<SWBuyConsoleComponent>(source, out var consoleComp))
            {
                if (add)
                {
                    consoleComp.ConnectedPallets.Add(ent.Owner);
                }
                else {
                    consoleComp.ConnectedPallets.Remove(ent.Owner);
                }
            }
        }
    }

    private void SpawnEntities(Entity<SWBuyConsoleComponent> ent, string proto, int amount)
    {
        var pallets = ent.Comp.ConnectedPallets.ToArray();
        var size = pallets.Length;

        for (int i = 0; i < amount; i++)
        {
            var coords = pallets[i % size].ToCoordinates();
            Spawn(proto, coords);
        }
    }
}
