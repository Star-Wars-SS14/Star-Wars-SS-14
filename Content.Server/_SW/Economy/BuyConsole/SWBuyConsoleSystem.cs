using Content.Server._SW.FinanceConsole;
using Content.Shared._SW.Economy;
using Content.Shared.Coordinates;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;
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
        SubscribeLocalEvent<SWBuyConsoleComponent, BeforeActivatableUIOpenEvent>(OnOpen);
        SubscribeLocalEvent<SWBuyConsoleComponent, SWTryPurchaseMessage>(OnTryPurchase);
    }



    private void OnMapInit(Entity<SWBuyConsoleComponent> ent, ref MapInitEvent args)
    {
        var comp = EnsureComp<SWBankAccountContainerComponent>(ent);
        foreach (var id in ent.Comp.AllowedCurrencies)
        {
            comp.CurrentAmount[id] = 0f;
        }
    }

    private void OnOpen(Entity<SWBuyConsoleComponent> ent, ref BeforeActivatableUIOpenEvent args)
    {
        if (!TryComp<SWBankAccountAccessorComponent>(ent, out var comp) || comp.Account == null)
        {
            return;
        }
        UpdateInterface(ent, comp.Account.Value);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<SWBuyConsoleComponent, SWBankAccountAccessorComponent>();
        while (query.MoveNext(out var uid, out var buyComp, out var comp))
        {
            if (comp.AccountInterfaceRequiresUpdate && comp.Account != null)
            {
                UpdateInterface(new Entity<SWBuyConsoleComponent>(uid, buyComp), comp.Account.Value);
                comp.AccountInterfaceRequiresUpdate = false;
            }
        }
    }
    private void UpdateInterface(Entity<SWBuyConsoleComponent> console, Entity<SWBankAccountContainerComponent> account)
    {
        var state = new SWBuyConsoleBounderUserInterfaceState();
        _swPayable.GetBalance(console.Owner, state);
        state.Products = console.Comp.Products;
        _uiSystem.SetUiState(console.Owner, SWBuyConsoleUIKey.Key, state);
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
            for (int i = 0; i < args.Amount; i++) {
                Spawn(proto.Product, ent.Owner.ToCoordinates());
            }
        }

        UpdateInterface(ent, account);
    }
}
