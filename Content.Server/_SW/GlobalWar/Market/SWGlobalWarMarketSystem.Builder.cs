using System.Linq;
using Content.Server._SW.GlobalWar.Politics;
using Content.Server._SW.Lathe.SWLathe;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Content.Shared.Popups;
using Content.Shared.Physics;
using Robust.Shared.Physics;
using Content.Shared.RCD;
using Content.Shared.RCD.Components;
using Content.Shared.RCD.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;

namespace Content.Server._SW.GlobalWar.Market;

public sealed partial class SWGlobalWarMarketSystem
{
    [Dependency] private readonly RCDSystem _builderRcd = default!;
    [Dependency] private readonly SharedPopupSystem _builderPopup = default!;
    private const string BuilderDeconstruct = "SWFactoryDeconstruct";
    private readonly HashSet<(EntityUid Tool, EntityUid Viewer)> _builderViewers = new();
    private TimeSpan _builderNextView;

    private void InitializeFactoryBuilder()
    {
        SubscribeLocalEvent<SWFactoryBuilderComponent, SWFactoryBuildEvent>(OnFactoryBuild);
        SubscribeLocalEvent<SWFactoryBuilderComponent, BoundUIOpenedEvent>(OnBuilderOpened);
        SubscribeLocalEvent<SWFactoryBuilderComponent, BoundUIClosedEvent>(OnBuilderClosed);
        Subs.BuiEvents<SWFactoryBuilderComponent>(RcdUiKey.Key, subs =>
            subs.Event<SWFactoryBuilderRefresh>(OnBuilderRefresh));
    }

    private void OnBuilderOpened(Entity<SWFactoryBuilderComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (!Equals(args.UiKey, RcdUiKey.Key))
            return;
        // Register on the authoritative open, independent of the timing of the client's initial request.
        _builderViewers.Add((ent.Owner, args.Actor));
        SendBuilderView(ent.Owner, args.Actor);
    }

    private void OnBuilderClosed(Entity<SWFactoryBuilderComponent> ent, ref BoundUIClosedEvent args)
    {
        if (Equals(args.UiKey, RcdUiKey.Key))
            _builderViewers.Remove((ent.Owner, args.Actor));
    }

    private bool BuilderActor(EntityUid tool, EntityUid body, out SWGlobalWarAssignment actor,
        out SWGlobalWarMarketState market)
    {
        actor = default!;
        market = default!;
        return Simulation?.Generation is { } generation && CanMutateCampaign(generation) &&
            _supplyHands.GetActiveItem(body) == tool && _hubBlocker.CanInteract(body, tool) &&
            TryComp<ActorComponent>(body, out var session) &&
            _assignments.TryGetAssignment(session.PlayerSession.UserId, out var assignment) &&
            _assignments.TryGetActor(session.PlayerSession, generation, assignment.Revision, out actor) &&
            FactoryOperator(actor) && _politics.CanAcquireMarketGoods(body, generation) &&
            TryGetTerminalMarket(body, out market) && market.Owner == actor.Faction;
    }

    private void OnBuilderRefresh(Entity<SWFactoryBuilderComponent> ent, ref SWFactoryBuilderRefresh args)
    {
        if (!_factoryUi.IsUiOpen(ent.Owner, RcdUiKey.Key, args.Actor))
            return;
        _builderViewers.Add((ent, args.Actor));
        SendBuilderView(ent, args.Actor);
    }

    private void UpdateBuilderViews()
    {
        if (_factoryTiming.CurTime < _builderNextView)
            return;
        _builderNextView = _factoryTiming.CurTime + TimeSpan.FromSeconds(1);
        foreach (var (tool, viewer) in _builderViewers.ToArray())
        {
            if (TerminatingOrDeleted(tool) || TerminatingOrDeleted(viewer) ||
                !_factoryUi.IsUiOpen(tool, RcdUiKey.Key, viewer))
                _builderViewers.Remove((tool, viewer));
            else
                SendBuilderView(tool, viewer);
        }
    }

    private void SendBuilderView(EntityUid tool, EntityUid viewer)
    {
        if (!TryComp<RCDComponent>(tool, out var rcd))
            return;
        var allowed = BuilderActor(tool, viewer, out var actor, out var market);
        var options = new List<SWFactoryBuilderOption>();
        foreach (var recipe in _prototypes.EnumeratePrototypes<SWFactoryBuildRecipePrototype>().OrderBy(r => r.ID))
        {
            if (!rcd.AvailablePrototypes.Any(p => p.Id == recipe.ID))
                continue;
            var costs = recipe.Materials.Select(p =>
            {
                var commodity = _prototypes.Index<SWGlobalWarCommodityPrototype>(p.Key);
                return new SWGlobalWarFactoryCost(Loc.GetString(commodity.Name), commodity.Entity,
                    p.Value, allowed && market.Goods.TryGetValue(p.Key, out var good)
                        ? good.Available / Simulation!.UnitsPerItem : 0);
            }).ToList();
            options.Add(new(recipe.ID, _prototypes.Index(recipe.Entity).Name, recipe.Entity, costs, BuilderPrice(recipe)));
        }
        var budget = allowed ? _ledger.GetBalance(actor.Faction, SWGlobalWarAccount.Industry).Available : 0;
        _factoryUi.ServerSendUiMessage(tool, RcdUiKey.Key,
            new SWFactoryBuilderSnapshot(rcd.ProtoId.Id, options, budget,
                Loc.GetString(allowed ? "sw-builder-ready" : "sw-builder-access"), allowed), viewer);
    }

    private long BuilderPrice(SWFactoryBuildRecipePrototype recipe) =>
        recipe.Materials.Sum(p => (long) p.Value * IndustrialPrice(p.Key));

    private void OnFactoryBuild(Entity<SWFactoryBuilderComponent> tool, ref SWFactoryBuildEvent args)
    {
        var reason = "sw-builder-access";
        var available = CompOrNull<RCDComponent>(tool)?.AvailablePrototypes;
        if (BuilderActor(tool, args.User, out var actor, out var market) &&
            TryComp<RCDComponent>(tool, out var rcd) && available != null && available.Contains(rcd.ProtoId))
        {
            // Notifications cannot observe a half-finished stock/payment/entity exchange.
            using var notifications = _ledger.DeferNotifications();
            _settling = true;
            try
            {
                var success = rcd.ProtoId.Id == BuilderDeconstruct
                    ? DismantleFactoryObject(args.User, args.Target, actor, market, ref reason)
                    : ConstructFactoryObject(tool, rcd, args, actor, market, ref reason);
                if (success)
                    reason = "sw-builder-success";
            }
            finally
            {
                _settling = false;
            }
        }
        _builderPopup.PopupEntity(Loc.GetString(reason), tool, args.User);
        if (_factoryUi.IsUiOpen(tool.Owner, RcdUiKey.Key, args.User))
            SendBuilderView(tool, args.User);
    }

    private bool ConstructFactoryObject(EntityUid tool, RCDComponent rcd, SWFactoryBuildEvent request,
        SWGlobalWarAssignment actor, SWGlobalWarMarketState market, ref string reason)
    {
        reason = "sw-builder-position";
        if (!_prototypes.TryIndex<SWFactoryBuildRecipePrototype>(rcd.ProtoId.Id, out var recipe) ||
            !request.Location.IsValid(EntityManager) ||
            _supplyTransforms.GetGrid(request.Location) is not { } grid ||
            grid != Transform(request.User).GridUid || !TryComp<MapGridComponent>(grid, out var gridData))
            return false;
        var tile = _hubMaps.TileIndicesFor(grid, gridData, request.Location);
        var coordinates = _hubMaps.GridTileToLocal(grid, gridData, tile);
        var center = _hubMaps.GetTileRef(grid, gridData, coordinates);
        if (!_builderRcd.IsRCDOperationStillValid(tool, rcd, grid, gridData, center, tile, null, request.User, false))
            return false;
        for (var x = -recipe.Radius; x <= recipe.Radius; x++)
        for (var y = -recipe.Radius; y <= recipe.Radius; y++)
        {
            var cell = tile + new Vector2i(x, y);
            var cellCoordinates = _hubMaps.GridTileToLocal(grid, gridData, cell);
            var tileRef = _hubMaps.GetTileRef(grid, gridData, cellCoordinates);
            if (tileRef.Tile.IsEmpty)
                return false;
            // Belts have sensor fixtures, so the ordinary RCD collision check alone allows duplicates.
            var occupants = new HashSet<EntityUid>();
            _factoryLookup.GetLocalEntitiesIntersecting(grid, cell, occupants, -0.05f, LookupFlags.Uncontained);
            if (occupants.Any(uid =>
                HasComp<Content.Shared.Mobs.Components.MobStateComponent>(uid) ||
                TryComp<FixturesComponent>(uid, out var fixtures) && fixtures.Fixtures.Values.Any(f =>
                    f.Hard && (f.CollisionLayer & (int) CollisionGroup.FullTileMask) != 0) ||
                Transform(uid).Anchored && _prototypes.EnumeratePrototypes<SWFactoryBuildRecipePrototype>()
                    .Any(r => r.Entity.Id == Prototype(uid)?.ID)))
                return false;
        }
        reason = "sw-builder-materials";
        foreach (var (commodity, quantity) in recipe.Materials)
            if (quantity <= 0 || !market.Goods.TryGetValue(commodity, out var good) ||
                good.Available < (long) quantity * Simulation!.UnitsPerItem)
                return false;
        var price = BuilderPrice(recipe);
        reason = "sw-builder-budget";
        if (price <= 0 || _ledger.GetBalance(actor.Faction, SWGlobalWarAccount.Industry).Available < price)
            return false;
        var reservations = new List<Guid>();
        EntityUid? built = null;
        var committed = false;
        try
        {
            foreach (var (commodity, quantity) in recipe.Materials)
            {
                var operation = Guid.NewGuid();
                if (!Simulation!.TryReserveStock(new(actor.Generation, operation, market.Planet, commodity,
                        quantity * Simulation.UnitsPerItem, actor.Faction, "factory-builder"), out _))
                    return false;
                reservations.Add(operation);
            }
            built = Spawn(recipe.Entity, coordinates);
            _supplyTransforms.SetLocalRotation(built.Value, rcd.ConstructionDirection.ToAngle());
            if (!Transform(built.Value).Anchored && !_supplyTransforms.AnchorEntity(built.Value))
            {
                reason = "sw-builder-position";
                return false;
            }
            if (!_ledger.TryExecuteSystem(new(actor.Generation, Guid.NewGuid(), 0, SWGlobalWarLedgerAction.Debit,
                    actor.Faction, SWGlobalWarAccount.Industry, price, Purpose: "factory-construction"), out _))
                return false;
            foreach (var operation in reservations)
                if (!Simulation!.TryCommitStock(actor.Generation, operation, out _))
                    throw new InvalidOperationException("Prepared factory material reservation could not commit.");
            committed = true;
            return true;
        }
        finally
        {
            if (!committed)
            {
                if (built is { } entity && !TerminatingOrDeleted(entity))
                    Del(entity);
                foreach (var operation in reservations)
                    Simulation!.TryCancelStock(actor.Generation, operation);
            }
        }
    }

    private bool DismantleFactoryObject(EntityUid user, EntityUid? target, SWGlobalWarAssignment actor,
        SWGlobalWarMarketState market, ref string reason)
    {
        reason = "sw-builder-target";
        if (target is not { } entity || TerminatingOrDeleted(entity) ||
            Transform(entity).GridUid != Transform(user).GridUid ||
            !_hubInteraction.InRangeUnobstructed(user, entity) ||
            _prototypes.EnumeratePrototypes<SWFactoryBuildRecipePrototype>()
                .FirstOrDefault(r => r.Entity.Id == Prototype(entity)?.ID) is not { } recipe)
            return false;
        reason = "sw-builder-empty";
        if (TryComp<ContainerManagerComponent>(entity, out var containers) &&
            _supplyContainers.GetAllContainers(entity, containers).Any(c => c.ContainedEntities.Count > 0))
            return false;
        var materials = new Dictionary<string, int>(recipe.Materials);
        if (TryComp<SWLatheComponent>(entity, out var lathe))
        {
            // The running recipe has already consumed its inputs. Recover them once, without producing its output.
            var stored = lathe.StashedMaterials.Select(p => new KeyValuePair<string, int>(p.Key.Id, p.Value));
            if (lathe.CurrentRecipeProducting is { } working)
                stored = stored.Concat(working.Materials);
            foreach (var (prototype, quantity) in stored)
            {
                if (quantity <= 0)
                    continue;
                var commodity = _prototypes.EnumeratePrototypes<SWGlobalWarCommodityPrototype>()
                    .FirstOrDefault(c => c.Entity.Id == prototype && market.Goods.ContainsKey(c.ID));
                if (commodity == null || (long) materials.GetValueOrDefault(commodity.ID) + quantity > int.MaxValue)
                    return false;
                materials[commodity.ID] = materials.GetValueOrDefault(commodity.ID) + quantity;
            }
        }
        reason = "sw-builder-capacity";
        foreach (var (commodity, quantity) in materials)
            if (!market.Goods.TryGetValue(commodity, out var good) ||
                (long) quantity * Simulation!.UnitsPerItem > good.Capacity - good.Stock - good.Incoming - good.ReservedIncoming)
                return false;
        var refund = (long) decimal.Floor(materials.Sum(p => (decimal) p.Value * IndustrialPrice(p.Key)) * 0.75m);
        if (!_ledger.TryExecuteSystem(new(actor.Generation, Guid.NewGuid(), 0, SWGlobalWarLedgerAction.Credit,
                actor.Faction, SWGlobalWarAccount.Industry, refund, Purpose: "factory-dismantling"), out _))
            return false;
        foreach (var (commodity, quantity) in materials)
            if (!Simulation!.DepositMarketStock(market.Planet, commodity, quantity * Simulation.UnitsPerItem))
                throw new InvalidOperationException("Prepared factory dismantling exceeded market capacity.");
        Del(entity);
        return true;
    }
}
