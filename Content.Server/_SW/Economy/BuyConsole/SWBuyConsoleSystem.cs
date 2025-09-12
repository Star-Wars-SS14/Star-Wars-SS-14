using Content.Server._SW.FinanceConsole;
using Content.Shared._SW.Economy;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.Economy.BuyConsole;

public sealed class SWBuyConsoleSystem : EntitySystem
{
    [Dependency] SWBasePayableConsoleSystem _swPayable = default!;
    [Dependency] private readonly UserInterfaceSystem _uiSystem = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWBuyConsoleComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<SWBuyConsoleComponent, BeforeActivatableUIOpenEvent>(OnOpen);
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
        while (query.MoveNext(out var uid, out var _, out var comp))
        {
            if (comp.AccountInterfaceRequiresUpdate && comp.Account != null)
            {
                UpdateInterface(uid, comp.Account.Value);
                comp.AccountInterfaceRequiresUpdate = false;
            }
        }
    }
    private void UpdateInterface(EntityUid console, Entity<SWBankAccountContainerComponent> account)
    {
        var state = new SWBuyConsoleBounderUserInterfaceState();
        ISawmill _s = Logger.GetSawmill("fuck");
        _s.Fatal("abbbbb");
        _swPayable.GetBalance(console, state);
        _uiSystem.SetUiState(console, SWBuyConsoleUIKey.Key, state);
    }
}
