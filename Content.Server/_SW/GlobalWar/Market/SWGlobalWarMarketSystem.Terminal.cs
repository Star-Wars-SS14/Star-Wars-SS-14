using System.Linq;
using Content.Server._SW.GlobalWar.Politics;
using Content.Server._SW.GlobalWar.Production;
using Content.Server.Power.EntitySystems;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Content.Shared.Stacks;
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
                goods.Add(new(commodity, name, ent.Comp.Warehouse ? market.FactoryStock.GetValueOrDefault(commodity) : stock.Available / (double) Simulation!.UnitsPerItem,
                    ent.Comp.Warehouse ? 0 : stock.Incoming / (double) Simulation!.UnitsPerItem,
                    ent.Comp.Warehouse ? 0 : ent.Comp.IndustrialSupply ? IndustrialPrice(commodity) :
                    IndustrialPrice(commodity)));
            }
            if (!powered)
                status += " · " + Loc.GetString("sw-global-war-factory-rejected-power");
            else if (ent.Comp.Arsenal && !CanBuyEquipment(actor))
                status += " · " + Loc.GetString("sw-global-war-terminal-service");
            else if (ent.Comp.Feedback.Length > 0)
                status += " · " + ent.Comp.Feedback;
        }
        _factoryUi.ServerSendUiMessage(ent.Owner, SWGlobalWarFactoryUiKey.Key,
            new SWGlobalWarFactorySnapshot(new(actor?.Generation ?? Guid.Empty, actor?.Revision ?? 0, actor != null && (!(ent.Comp.Warehouse || ent.Comp.IndustrialSupply || ent.Comp.IndustrialOutput) || FactoryOperator(actor)),
                false, false, default, status, [], [], [], 0, 1, [], false, ent.Comp.Arsenal, powered,
                marketGoods: goods, canBuyEquipment: powered && actor != null && (CanBuyEquipment(actor) || FactoryOperator(actor)),
                outputPallets: actor == null ? 0 : FindMarketPallets(ent, false).Count,
                inputPallets: actor == null ? 0 : FindMarketPallets(ent, true).Count,
                warehouse: ent.Comp.Warehouse, industrialSupply: ent.Comp.IndustrialSupply,
                industrialOutput: ent.Comp.IndustrialOutput, purchaseRates: new(ent.Comp.PurchaseRates),
                canConfigurePurchases: actor != null && FactoryOperator(actor) && ent.Comp.AutomaticMarketAccess),
                subscription, GetNetEntity(viewer)), viewer);
    }

    private bool ExchangeMarketItem(Entity<SWGlobalWarFactoryComponent> ent, SWGlobalWarAssignment actor, Guid request,
        SWGlobalWarFactoryAction action, string commodity, int quantity, ref string rejected)
    {
        if (!this.IsPowered(ent, EntityManager) || !TryGetTerminalMarket(ent, out var market))
        {
            rejected = "sw-global-war-factory-rejected-power";
            return false;
        }
        if (action == SWGlobalWarFactoryAction.ConfigureIndustrialRate ||
            ent.Comp.IndustrialSupply || ent.Comp.IndustrialOutput || ent.Comp.Warehouse)
            return IndustrialRequest(ent, actor, request, market, action, commodity, quantity, ref rejected);
        if (action == SWGlobalWarFactoryAction.DepositMarket)
            return DepositPalletItems(ent, actor, market, ref rejected);
        if (commodity == null || !market.Goods.TryGetValue(commodity, out var good) ||
            quantity <= 0 || quantity > good.Available / Simulation!.UnitsPerItem)
            return false;
        var military = MilitaryGoods.TryGetValue(commodity, out var supply);
        if (ent.Comp.Arsenal && !military || military && !CanBuyEquipment(actor) || !military && IndustrialPrice(commodity) > 0 && !FactoryOperator(actor))
            return false;
        var pallets = FindMarketPallets(ent, deposit: false);
        if (pallets.Count == 0)
        {
            rejected = "sw-global-war-terminal-no-output";
            return false;
        }
        var unitPrice = IndustrialPrice(commodity);
        var account = military ? SWGlobalWarAccount.War : SWGlobalWarAccount.Industry;
        var price = (long) unitPrice * quantity;
        if (price > 0 && _ledger.GetBalance(actor.Faction, account).Available < price)
        {
            rejected = "sw-global-war-terminal-no-funds";
            return false;
        }
        if (!Simulation!.TryReserveStock(new(actor.Generation, request, market.Planet, commodity,
                quantity * Simulation.UnitsPerItem, actor.Faction, "market-withdrawal"), out _))
        {
            rejected = "sw-global-war-factory-rejected-stock";
            return false;
        }
        var prototype = military ? SupplyPrototype(supply.Kind, actor.Faction, supply.Bacta) :
            _prototypes.Index<SWGlobalWarCommodityPrototype>(commodity).Entity;
        var escrow = CreateSupplyEscrow();
        var output = new List<EntityUid>();
        var committed = false;
        try
        {
            var remaining = quantity;
            while (remaining > 0)
            {
                var item = Spawn(prototype, Transform(escrow).Coordinates);
                output.Add(item);
                var count = 1;
                if (!military && TryComp<StackComponent>(item, out var stack))
                {
                    count = Math.Min(remaining, _supplyStacks.GetMaxCount(stack));
                    _supplyStacks.SetCount(item, count, stack);
                }
                remaining -= count;
                if (!military)
                    continue;
                if (_supplyContainers.TryGetContainer(item, "gun_magazine", out var magazine))
                    foreach (var loaded in magazine.ContainedEntities.ToArray())
                        Del(loaded);
                var receipt = EnsureComp<SWGlobalWarMarketReceiptComponent>(item);
                receipt.Generation = actor.Generation;
                receipt.Commodity = commodity;
                receipt.Payer = actor.Faction;
                receipt.Price = unitPrice;
            }
            if (price > 0 && !_ledger.TryExecuteSystem(new(actor.Generation, Guid.NewGuid(), 0,
                    SWGlobalWarLedgerAction.Debit, actor.Faction, account, price,
                    Purpose: "planetary-market-purchase"), out _))
            {
                rejected = "sw-global-war-terminal-no-funds";
                return false;
            }
            if (!Simulation.TryCommitStock(actor.Generation, request, out _))
                throw new InvalidOperationException("Reserved market withdrawal could not commit.");
            committed = true;
            for (var i = 0; i < output.Count; i++)
                _supplyTransforms.SetCoordinates(output[i], Transform(pallets[i % pallets.Count]).Coordinates);
            if (military && supply.Kind is (SWGlobalWarSupplyKind.T1 or SWGlobalWarSupplyKind.T2 or SWGlobalWarSupplyKind.Armor))
                _issuedKits.Add(actor.Id);
            return true;
        }
        finally
        {
            if (!committed)
            {
                foreach (var item in output)
                    if (!TerminatingOrDeleted(item))
                        Del(item);
                Simulation.TryCancelStock(actor.Generation, request);
            }
            Del(escrow);
        }
    }
}
