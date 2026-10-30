using Content.Server._SW.Economy.FinanceConsole;
using Content.Server._SW.FinanceConsole;
using Content.Shared._SW;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Popups;
using Content.Shared.Random.Rules;
using Content.Shared.Stacks;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Robust.Server.GameObjects;
using Content.Shared._SW.Economy.BankSystem;
using Content.Shared._SW.Economy.Messages;
using Content.Shared._SW.Economy.Prototypes;
using Content.Server._SW.Economy.BankSystem;
using Content.Server.Popups;
namespace Content.Server._SW.Economy;

public sealed class SWBasePayableConsoleSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] SharedStackSystem _stackSystem = default!;
    [Dependency] private readonly UserInterfaceSystem _uiSystem = default!;
    [Dependency] SharedHandsSystem _handsSystem = default!;
    [Dependency] SWBankSystem _bankSystem = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    ISawmill _s = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWBankAccountAccessorComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<SWBankAccountAccessorComponent, SWTryPayoutMessage>(OnTryPayout);
        SubscribeLocalEvent<SWBankAccountAccessorComponent, SWTryAddMoneyMessage>(OnAddMoney);
        //SubscribeLocalEvent<SWBankAccountAccessorComponent, BeforeActivatableUIOpenEvent>(OnOpen, before: [typeof(SWFinanceConsoleSystem)]);
        _s = Logger.GetSawmill("SWPayable");
    }

    //public override void Update(float frameTime)
    //{
    //    base.Update(frameTime);
    //    var query = EntityQueryEnumerator<SWBankAccountAccessorComponent>();
    //    while (query.MoveNext(out var uid, out var comp))
    //    {
    //        if (comp.Account != null)
    //        {
    //            UpdateInterface(new Entity<SWBankAccountAccessorComponent>(uid, comp));
    //            comp.AccountInterfaceRequiresUpdate = false;
    //        }
    //    }
    //}
    private void OnMapInit(Entity<SWBankAccountAccessorComponent> ent, ref MapInitEvent args)
    {
        var ev = new SWGetAccountEvent();
        RaiseLocalEvent(ent, ref ev);
        if (!ev.Handled)
        {
            if (TryComp<SWBankAccountContainerComponent>(ent, out var comp))
            {
                ev.Account = new Entity<SWBankAccountContainerComponent>(ent, comp);
                ev.Handled = true;
            }
            else if (HasComp<SWFinanceConsoleComponent>(ent))
            {
                // A finance console uses its faction's account, and presets without faction rules have no factions.
                _s.Debug($"{ent.Owner} has no faction to take an account from");
            }
            else
            {
                _s.Error($"{ent.Owner} account is not set");
            }
        }
        if (ev.Handled)
        {
            ent.Comp.Account = ev.Account;
        }
        ent.Comp.AccountInterfaceRequiresUpdate = true;
        //UpdateInterface(ent);
    }
    private void OnTryPayout(Entity<SWBankAccountAccessorComponent> ent, ref SWTryPayoutMessage args)
    {
        if (ent.Comp.Account == null)
        {
            Refuse(args.Actor, "sw-console-no-account");
            return;
        }

        if (!ent.Comp.CanPayOut)
        {
            Refuse(args.Actor, "sw-economy-payout-disabled");
            return;
        }

        if (!IsValidPayout(args.Requests))
        {
            Refuse(args.Actor, "sw-economy-payout-invalid");
            return;
        }

        if (!_bankSystem.TryPayAll(args.Requests, ent.Comp.Account.Value.Comp.CurrentAmount))
        {
            Refuse(args.Actor, "sw-economy-not-enough-money");
        }
        else
        {
            foreach (var (currency, amount) in args.Requests)
            {
                var proto = _prototypeManager.Index(currency);
                var spawnedCash = Spawn(proto.CashEntity, Transform(ent).Coordinates);
                _stackSystem.SetCount(spawnedCash, (int)amount);
            }
            _uiSystem.ServerSendUiMessage(ent.Owner, args.UiKey, new SWConsoleActionSucceededMessage(SWConsoleAction.Payout), args.Actor);
        }
        ent.Comp.AccountInterfaceRequiresUpdate = true;
        //UpdateInterface(ent);
    }

    /// <summary>
    /// Cash stacks hold a whole positive count, so a payout request must be one too.
    /// </summary>
    private bool IsValidPayout(Dictionary<ProtoId<SWCurrencyPrototype>, float> requests)
    {
        if (requests.Count == 0)
        {
            return false;
        }

        foreach (var amount in requests.Values)
        {
            if (!_bankSystem.IsValidAmount(amount)
                || amount < 1
                || amount > int.MaxValue
                || amount != MathF.Floor(amount))
            {
                return false;
            }
        }

        return true;
    }

    private void OnAddMoney(Entity<SWBankAccountAccessorComponent> ent, ref SWTryAddMoneyMessage args)
    {
        if (ent.Comp.Account == null)
        {
            Refuse(args.Actor, "sw-console-no-account");
            return;
        }


        if (!ent.Comp.CanPayIn)
        {
            Refuse(args.Actor, "sw-economy-deposit-disabled");
            return;
        }

        if (args.Actor is not { Valid: true } user)
            return;

        var deposited = false;
        foreach (var entity in _handsSystem.EnumerateHeld(user))
        {
            if (!TryComp<StackComponent>(entity, out var comp))
            {
                continue;
            }

            var proto = _bankSystem.GetCurrencyProto(entity);

            var bankComp = ent.Comp.Account.Value.Comp;
            if (proto != null)
            {
                if (!bankComp.AllowedCurrencies.Contains(proto.ID))
                {
                    continue;
                }

                if (!bankComp.CurrentAmount.ContainsKey(proto))
                {
                    bankComp.CurrentAmount[proto] = 0;
                }

                bankComp.CurrentAmount[proto] += comp.Count;
                QueueDel(entity);
                deposited = true;
            }
        }

        if (!deposited)
        {
            Refuse(user, "sw-economy-deposit-nothing");
        }
        ent.Comp.AccountInterfaceRequiresUpdate = true;
        //UpdateInterface(ent);
    }
    //private void UpdateInterface(Entity<SWBankAccountAccessorComponent> ent)
    //{
    //    if (ent.Comp.Account == null)
    //    {
    //        return;
    //    }

    //    var state = new SWBasePayableBounderUserInterfaceState();
    //    state.CurrentAmount = ent.Comp.Account.Value.Comp.CurrentAmount;
    //    _s.Fatal(state.CurrentAmount.Keys.Count.ToString());
    //    //_s.Fatal(ent.Comp.UiKey.ToString());
    //    //_uiSystem.SetUiState(ent.Owner, SWFinanceConsoleUIKey.Key, state);
    //    //_resolver.RequestUiUpdate(ent.Owner, SWFinanceConsoleUIKey.Key, state);
    //}

    /// <summary>
    /// Shows the player why a console said no.
    /// </summary>
    public void Refuse(EntityUid actor, LocId reason)
    {
        _popup.PopupCursor(Loc.GetString(reason), actor, PopupType.Medium);
    }

    public Dictionary<ProtoId<SWCurrencyPrototype>, float> GetBalance(Entity<SWBankAccountAccessorComponent?> console)
    {
        console.Comp = Comp<SWBankAccountAccessorComponent>(console);
        if (console.Comp.Account == null)
        {
            return new Dictionary<ProtoId<SWCurrencyPrototype>, float>();
        }
        return console.Comp.Account.Value.Comp.CurrentAmount;
    }
}

[ByRefEvent]
public sealed class SWGetAccountEvent : HandledEntityEventArgs
{
    public Entity<SWBankAccountContainerComponent> Account;
}

