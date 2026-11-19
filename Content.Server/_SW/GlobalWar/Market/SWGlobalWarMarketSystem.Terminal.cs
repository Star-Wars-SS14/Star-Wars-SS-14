using System.Linq;
using Content.Server._SW.GlobalWar.Politics;
using Content.Server._SW.GlobalWar.Production;
using Content.Server.Power.EntitySystems;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Robust.Shared.Prototypes;

namespace Content.Server._SW.GlobalWar.Market;

[RegisterComponent]
public sealed partial class SWGlobalWarMarketReceiptComponent : Component
{
    public Guid Generation;
    public SWGlobalWarFaction Payer;
    public string Commodity = string.Empty;
    public int Price;
}

public sealed partial class SWGlobalWarMarketSystem
{
    private readonly HashSet<Guid> _marketExchanges = new();
    private static readonly Dictionary<string, (SWGlobalWarSupplyKind Kind, bool Bacta)> MilitaryGoods =
        Enum.GetValues<SWGlobalWarSupplyKind>().Where(k => k != SWGlobalWarSupplyKind.AdvancedElectronics)
            .Select(k => (Kind: k, Bacta: false)).Append((Kind: SWGlobalWarSupplyKind.Medicine, Bacta: true))
            .ToDictionary(p => SupplyCommodity(p.Kind, p.Bacta));

    private static bool CanBuyEquipment(SWGlobalWarAssignment actor) =>
        actor.Role is SWGlobalWarRole.Head or SWGlobalWarRole.WarMinister or SWGlobalWarRole.Soldier or
            SWGlobalWarRole.Pilot or SWGlobalWarRole.SeniorOfficer or SWGlobalWarRole.JuniorOfficer or
            SWGlobalWarRole.Intelligence or SWGlobalWarRole.ForceUser;

    private bool TryGetTerminalMarket(EntityUid terminal, out SWGlobalWarMarketState market)
    {
        market = default!;
        return Simulation != null && _hubPlanets.TryGetPhysicalLocation(terminal, out var planet, out var location) &&
            location == SWGlobalWarPlanetLocation.Surface &&
            Simulation.Markets.TryGetValue(Comp<SWGlobalWarPlanetComponent>(planet).Prototype.Id, out market!);
    }

    private void SendTerminalView(Entity<SWGlobalWarFactoryComponent> ent, EntityUid viewer, Guid subscription,
        SWGlobalWarAssignment? actor)
    {
        var goods = new List<SWGlobalWarTerminalGood>();
        var powered = this.IsPowered(ent, EntityManager);
        var status = Loc.GetString("sw-global-war-factory-unavailable");
        if (actor != null && TryGetTerminalMarket(ent, out var market))
        {
            status = _prototypes.Index<SWGlobalWarPlanetPrototype>(market.Planet).Name;
            foreach (var (commodity, stock) in market.Goods)
            {
                var military = MilitaryGoods.TryGetValue(commodity, out var item);
                if (ent.Comp.Arsenal && !military)
                    continue;
                var name = Loc.GetString(_prototypes.Index<SWGlobalWarCommodityPrototype>(commodity).Name);
                goods.Add(new(commodity, name, stock.Available / (double) Simulation!.UnitsPerItem,
                    stock.Incoming / (double) Simulation.UnitsPerItem,
                    military ? SWGlobalWarSupplyProgram.UnitPrice(item.Kind, item.Bacta) : 0));
            }
            if (!powered)
                status += " · " + Loc.GetString("sw-global-war-factory-rejected-power");
            else if (ent.Comp.Arsenal && !CanBuyEquipment(actor))
                status += " · " + Loc.GetString("sw-global-war-terminal-service");
            else if (ent.Comp.Feedback.Length > 0)
                status += " · " + ent.Comp.Feedback;
        }
        _factoryUi.ServerSendUiMessage(ent.Owner, SWGlobalWarFactoryUiKey.Key,
            new SWGlobalWarFactorySnapshot(new(actor?.Generation ?? Guid.Empty, actor?.Revision ?? 0, actor != null,
                false, false, default, status, [], [], [], 0, 1, [], false, ent.Comp.Arsenal, powered,
                marketGoods: goods, canBuyEquipment: powered && actor != null && CanBuyEquipment(actor)),
                subscription, GetNetEntity(viewer)), viewer);
    }

    private bool ExchangeMarketItem(Entity<SWGlobalWarFactoryComponent> ent, SWGlobalWarAssignment actor, Guid request,
        SWGlobalWarFactoryAction action, string commodity, ref string rejected)
    {
        if (!this.IsPowered(ent, EntityManager) || !TryGetTerminalMarket(ent, out var market))
        {
            rejected = "sw-global-war-factory-rejected-power";
            return false;
        }
        if (action == SWGlobalWarFactoryAction.DepositMarket)
            return DepositHeldItem(ent, actor, market, ref rejected);
        if (commodity == null || !market.Goods.ContainsKey(commodity))
            return false;
        var military = MilitaryGoods.TryGetValue(commodity, out var supply);
        if (ent.Comp.Arsenal && !military || military && !CanBuyEquipment(actor))
            return false;
        var price = military ? SWGlobalWarSupplyProgram.UnitPrice(supply.Kind, supply.Bacta) : 0;
        if (price > 0 && _ledger.GetBalance(actor.Faction, SWGlobalWarAccount.War).Available < price)
        {
            rejected = "sw-global-war-terminal-no-funds";
            return false;
        }
        if (!Simulation!.TryReserveStock(new(actor.Generation, request, market.Planet, commodity,
                Simulation.UnitsPerItem, actor.Faction, "market-withdrawal"), out _))
        {
            rejected = "sw-global-war-factory-rejected-stock";
            return false;
        }
        var prototype = military ? SupplyPrototype(supply.Kind, actor.Faction, supply.Bacta) :
            _prototypes.Index<SWGlobalWarCommodityPrototype>(commodity).Entity;
        var item = Spawn(prototype, Transform(actor.Body).Coordinates);
        if (price > 0 && !_ledger.TryExecuteSystem(new(actor.Generation, Guid.NewGuid(), 0,
                SWGlobalWarLedgerAction.Debit, actor.Faction, SWGlobalWarAccount.War, price,
                Purpose: "planetary-market-purchase"), out _))
        {
            Del(item);
            Simulation.TryCancelStock(actor.Generation, request);
            rejected = "sw-global-war-terminal-no-funds";
            return false;
        }
        if (!Simulation.TryCommitStock(actor.Generation, request, out _))
            throw new InvalidOperationException("Reserved market withdrawal could not commit.");
        if (military)
        {
            if (_supplyContainers.TryGetContainer(item, "gun_magazine", out var magazine))
                foreach (var loaded in magazine.ContainedEntities.ToArray())
                    Del(loaded);
            var receipt = EnsureComp<SWGlobalWarMarketReceiptComponent>(item);
            receipt.Generation = actor.Generation;
            receipt.Commodity = commodity;
            receipt.Payer = actor.Faction;
            receipt.Price = price;
            if (supply.Kind is SWGlobalWarSupplyKind.T1 or SWGlobalWarSupplyKind.T2 or SWGlobalWarSupplyKind.Armor)
                _issuedKits.Add(actor.Id);
        }
        _supplyHands.TryPickupAnyHand(actor.Body, item);
        return true;
    }

    private bool DepositHeldItem(Entity<SWGlobalWarFactoryComponent> ent, SWGlobalWarAssignment actor,
        SWGlobalWarMarketState market, ref string rejected)
    {
        if (!_supplyHands.TryGetActiveItem(actor.Body, out var held) || held is not { } item ||
            TerminatingOrDeleted(item) || !_supplyHands.CanDrop(actor.Body, item))
        {
            rejected = "sw-global-war-factory-rejected-no-item";
            return false;
        }
        foreach (var (commodity, stock) in market.Goods)
        {
            var military = MilitaryGoods.TryGetValue(commodity, out var supply);
            if (ent.Comp.Arsenal && !military)
                continue;
            if (military)
            {
                if (!IsSuitableSupplyItem(item, supply.Kind, actor.Faction, supply.Bacta) ||
                    _supplyContainers.TryGetContainer(item, "gun_magazine", out var magazine) &&
                    magazine.ContainedEntities.Count > 0)
                    continue;
            }
            else if (!MatchesSupplyMaterial(item, _prototypes.Index<SWGlobalWarCommodityPrototype>(commodity).Entity.Id))
                continue;

            var units = _supplyStacks.GetCount(item);
            var space = stock.Capacity - stock.Stock - stock.Incoming - stock.ReservedIncoming;
            if (units <= 0 || units > space / Simulation!.UnitsPerItem)
            {
                rejected = FactoryRejectedSpace;
                return false;
            }
            if (TryComp<SWGlobalWarMarketReceiptComponent>(item, out var receipt) &&
                receipt.Generation == actor.Generation && receipt.Commodity == commodity &&
                !_ledger.TryExecuteSystem(new(actor.Generation, Guid.NewGuid(), 0, SWGlobalWarLedgerAction.Credit,
                    receipt.Payer, SWGlobalWarAccount.War, receipt.Price, Purpose: "planetary-market-refund"), out _))
                return false;
            if (!Simulation.DepositMarketStock(market.Planet, commodity, units * Simulation.UnitsPerItem))
                throw new InvalidOperationException("Prepared market deposit exceeded available capacity.");
            _supplyItems.Remove(item);
            Del(item);
            return true;
        }
        rejected = FactoryRejectedItem;
        return false;
    }
}
