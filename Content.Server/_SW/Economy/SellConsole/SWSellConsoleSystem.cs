using Content.Server.Cargo.Components;
using Content.Shared.Cargo.BUI;
using Content.Shared.Cargo.Components;
using Content.Shared.Cargo;
using Content.Shared.Cargo.Events;
using Content.Shared.Cargo.Prototypes;
using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Robust.Server.GameObjects;
using Content.Shared._SW.Economy;
using Content.Server.Cargo.Systems;
using Content.Shared.Mobs.Components;
using Content.Shared.Coordinates;
using System.ComponentModel;
using Content.Server.Stack;

namespace Content.Server._SW.Economy.SellConsole;

public sealed class SWSellConsoleSystem : EntitySystem
{
    [Dependency] private readonly UserInterfaceSystem _uiSystem = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly CargoSystem _cargoSystem = default!;
    [Dependency] private readonly PricingSystem _pricing = default!;
    [Dependency] private readonly SWSharedBankSystem _bankSystem = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly StackSystem _stack = default!;

    private EntityQuery<TransformComponent> _xformQuery;
    private EntityQuery<MobStateComponent> _mobQuery;
    private EntityQuery<CargoSellBlacklistComponent> _blacklistQuery;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWSellConsoleComponent, CargoPalletSellMessage>(OnPalletSale);
        SubscribeLocalEvent<SWSellConsoleComponent, CargoPalletAppraiseMessage>(OnPalletAppraise);
        _xformQuery = GetEntityQuery<TransformComponent>();
        _mobQuery = GetEntityQuery<MobStateComponent>();
        _blacklistQuery = GetEntityQuery<CargoSellBlacklistComponent>();
    }
    private void OnPalletAppraise(Entity<SWSellConsoleComponent> ent, ref CargoPalletAppraiseMessage args)
    {
        UpdatePalletConsoleInterface(ent);
    }

    private void UpdatePalletConsoleInterface(Entity<SWSellConsoleComponent> ent)
    {
        if (Transform(ent).GridUid is not { } gridUid)
        {
            _uiSystem.SetUiState(ent.Owner,
                SWSellConsoleUIKey.Key,
                new SWCargoPalletConsoleInterfaceState(0, 0,
                                            false,
                                            ent.Comp.OutputCurrency));
            return;
        }
        GetPalletGoods(ent, out var toSell, out var goods);
        var totalAmount = goods.Sum(t => t.Item3);
        _uiSystem.SetUiState(ent.Owner,
            SWSellConsoleUIKey.Key,
            new SWCargoPalletConsoleInterfaceState(
                (int)totalAmount,
                toSell.Count,
                true,
                ent.Comp.OutputCurrency));
    }
    private void OnPalletSale(Entity<SWSellConsoleComponent> ent, ref CargoPalletSellMessage args)
    {
        GetPalletGoods(ent, out var toSell, out var goods);
        var totalAmount = goods.Sum(t => t.Item3);
        var transferredAmount = _bankSystem.TransferCurrency(totalAmount,
                                                             ent.Comp.StaticPriceCurrency,
                                                             ent.Comp.OutputCurrency);

        foreach (var entity in toSell)
        {
            QueueDel(entity);
        }

        var curProto = _prototypeManager.Index(ent.Comp.OutputCurrency);
        var spawned = Spawn(curProto.CashEntity, ent.Owner.ToCoordinates());
        _stack.SetCount(spawned, (int)transferredAmount);
        UpdatePalletConsoleInterface(ent);
    }

    private void GetPalletGoods(Entity<SWSellConsoleComponent> ent, out HashSet<EntityUid> toSell, out HashSet<(EntityUid, OverrideSellComponent?, double)> goods)
    {
        goods = new HashSet<(EntityUid, OverrideSellComponent?, double)>();
        toSell = new HashSet<EntityUid>();
        HashSet<EntityUid> setEnts = new();

        foreach (var palletUid in GetCargoPallets(ent))
        {
            // Containers should already get the sell price of their children so can skip those.
            setEnts.Clear();

            _lookup.GetEntitiesIntersecting(
                palletUid,
                setEnts,
                LookupFlags.Dynamic | LookupFlags.Sundries);

            foreach (var e in setEnts)
            {
                // Dont sell:
                // - anything already being sold
                // - anything anchored (e.g. light fixtures)
                // - anything blacklisted (e.g. players).
                if (toSell.Contains(e) ||
                    _xformQuery.TryGetComponent(ent, out var xform) &&
                    (xform.Anchored || !CanSell(ent, xform)))
                {
                    continue;
                }

                if (_blacklistQuery.HasComponent(ent))
                    continue;

                var price = _pricing.GetPrice(ent);
                if (price == 0)
                    continue;
                toSell.Add(ent);
                goods.Add((ent, CompOrNull<OverrideSellComponent>(ent), price));
            }
        }
    }
    private bool CanSell(EntityUid uid, TransformComponent xform)
    {
        if (_mobQuery.HasComponent(uid))
        {
            return false;
        }

        // Recursively check for mobs at any point.
        var children = xform.ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (!CanSell(child, _xformQuery.GetComponent(child)))
                return false;
        }

        return true;
    }
    private List<EntityUid> GetCargoPallets(Entity<SWSellConsoleComponent> ent)
    {
        return ent.Comp.LinkedPallets;
    }

}
