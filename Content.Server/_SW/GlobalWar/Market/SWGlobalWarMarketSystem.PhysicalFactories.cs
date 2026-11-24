using System.Linq;
using Content.Server._SW.GlobalWar.Politics;
using Content.Server._SW.GlobalWar.Production;
using Content.Server.Power.EntitySystems;
using Content.Server.GameTicking;
using Content.Shared.Access.Systems;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Content.Shared.GameTicking;
using Content.Shared.Access.Components;
using Content.Shared.Item;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Power.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._SW.GlobalWar.Market;

public sealed partial class SWGlobalWarMarketSystem
{
    [Dependency] private readonly UserInterfaceSystem _factoryUi = default!;
    [Dependency] private readonly IGameTiming _factoryTiming = default!;
    [Dependency] private readonly SharedBatterySystem _factoryBatteries = default!;
    private readonly Dictionary<(EntityUid Store, EntityUid Actor), Guid> _factorySubscriptions = new();
    private readonly Dictionary<(EntityUid Store, EntityUid Actor), int> _factoryPages = new();
    [Dependency] private readonly SharedIdCardSystem _factoryIds = default!;
    private readonly Dictionary<(Guid Assignment, SWGlobalWarSupplyKind Kind), HashSet<Guid>> _factoryIssued = new();
    private TimeSpan _factoryNextView;
    private const string FactoryRejectedOperator = "sw-global-war-factory-rejected-operator";
    private const string FactoryRejectedBusy = "sw-global-war-factory-rejected-busy";
    private const string FactoryRejectedItem = "sw-global-war-factory-rejected-item";
    private const string FactoryRejectedSpace = "sw-global-war-factory-rejected-space";

    private void InitializePhysicalFactories()
    {
        SubscribeLocalEvent<SWGlobalWarShipArrivalCommittedEvent>(OnPhysicalFreightArrival);
        Subs.BuiEvents<SWGlobalWarFactoryComponent>(SWGlobalWarFactoryUiKey.Key, subs =>
        {
            subs.Event<SWGlobalWarFactoryOpen>(OnFactoryOpen);
            subs.Event<SWGlobalWarFactoryRequest>(OnFactoryRequest);
        });
    }

    private void ResetPhysicalFactories()
    {
        var query = EntityManager.AllEntityQueryEnumerator<SWGlobalWarFactoryComponent>();
        while (query.MoveNext(out _, out var factory))
            ReleaseFactoryLease(factory);
        _factorySubscriptions.Clear();
        _factoryPages.Clear();
        _factoryIssued.Clear();
        _factoryMaterializations.Clear();
        _factoryRecipeWork.Clear();
        _physicalCargo.Clear();
        _physicalFlights.Clear();
        _physicalMiningDeposits.Clear();
        _physicalMiningSites.Clear();
    }

    private bool BindFactory(Entity<SWGlobalWarFactoryComponent> ent, out SWGlobalWarFaction owner)
    {
        owner = default;
        if (Simulation is not { Generation: { } generation } model || !CanMutateCampaign(generation) || !Transform(ent).Anchored ||
            !_hubPlanets.TryGetPhysicalLocation(ent, out var planet, out var location) ||
            location != SWGlobalWarPlanetLocation.Surface)
            return false;
        var world = Comp<SWGlobalWarPlanetComponent>(planet).Prototype;
        if (!model.Markets.TryGetValue(world.Id, out var market) ||
            !ent.Comp.Recruitment && !TryRegisterSupplyStore(generation, ent, ent.Comp.Arsenal))
            return false;
        owner = market.Owner;
        if (owner is not (SWGlobalWarFaction.Republic or SWGlobalWarFaction.Empire))
            return false;
        if (ent.Comp.Generation != generation)
        {
            ent.Comp.Generation = generation;
            ent.Comp.Enabled = (ent.Comp.Factory || ent.Comp.Mining) && ent.Comp.AutoStart;
            ent.Comp.MiningTime = TimeSpan.Zero;
            ent.Comp.MiningOutput = null;
            ent.Comp.MiningEscrow = null;
            ent.Comp.Work.Clear();
            ent.Comp.Feedback = string.Empty;
            ReleaseFactoryLease(ent.Comp);
            if (ent.Comp.Enabled && ent.Comp.Factory)
            {
                TryRegisterSupplyConsumer(generation, ent, ent.Comp.Kind);
                foreach (var kind in ent.Comp.ExtraKinds)
                    TryRegisterSupplyConsumer(generation, ent, kind);
            }
        }
        return ent.Comp.Recruitment || TryGetSupplyStore(generation, ent, owner, out _, out _);
    }

    private bool FactoryActor(Entity<SWGlobalWarFactoryComponent> ent, EntityUid body,
        out SWGlobalWarAssignment actor)
    {
        actor = default!;
        return BindFactory(ent, out var owner) && TryComp<ActorComponent>(body, out var session) &&
            _assignments.TryGetAssignment(session.PlayerSession.UserId, out var current) &&
            _assignments.TryGetActor(session.PlayerSession, ent.Comp.Generation, current.Revision, out actor) &&
            actor.Faction == owner && _politics.CanAcquireMarketGoods(body, ent.Comp.Generation) &&
            _hubBlocker.CanInteract(body, ent) && _hubInteraction.InRangeUnobstructed(body, ent.Owner, range: 2f);
    }

    private static bool FactoryOperator(SWGlobalWarAssignment actor)
    {
        return actor.Role is SWGlobalWarRole.Head or SWGlobalWarRole.IndustryMinister or
            SWGlobalWarRole.IndustryWorker or SWGlobalWarRole.LogisticsMinister or SWGlobalWarRole.LogisticsWorker;
    }

    private void OnFactoryOpen(Entity<SWGlobalWarFactoryComponent> ent, ref SWGlobalWarFactoryOpen args)
    {
        if (args.Subscription == Guid.Empty || !_factoryUi.IsUiOpen(ent.Owner, SWGlobalWarFactoryUiKey.Key, args.Actor))
            return;
        _factorySubscriptions[(ent, args.Actor)] = args.Subscription;
        _factoryPages[(ent, args.Actor)] = 0;
        SendFactoryView(ent, args.Actor);
    }

    private void OnFactoryRequest(Entity<SWGlobalWarFactoryComponent> ent, ref SWGlobalWarFactoryRequest args)
    {
        if (args.Request == Guid.Empty || !Enum.IsDefined(args.Action) || !Enum.IsDefined(args.Kind) ||
            !_factoryUi.IsUiOpen(ent.Owner, SWGlobalWarFactoryUiKey.Key, args.Actor) ||
            !FactoryActor(ent, args.Actor, out var actor) || actor.Generation != args.Generation ||
            actor.Revision != args.Revision)
            return;
        var success = false;
        // Without a more specific reason below, a line control refused for the role names the role.
        var rejected = args.Action is SWGlobalWarFactoryAction.Start or SWGlobalWarFactoryAction.Stop &&
            (ent.Comp.Factory || ent.Comp.Mining) && !FactoryOperator(actor)
                ? FactoryRejectedOperator : "sw-global-war-factory-rejected";
        var kind = args.Kind;
        switch (args.Action)
        {
            case SWGlobalWarFactoryAction.Start when ent.Comp.Mining && FactoryOperator(actor):
                ent.Comp.Enabled = true;
                success = true;
                break;
            case SWGlobalWarFactoryAction.Start when ent.Comp.Factory && FactoryOperator(actor):
                if (!FactoryKinds(ent).Contains(kind))
                {
                    rejected = "sw-global-war-factory-rejected-kind";
                    break;
                }
                if (GetSupplyProductionWork(args.Generation, ent).Any(p => p.Request.Kind != kind) ||
                    _factoryRecipeWork.Values.Any(w => w.Store == ent.Owner && !w.Complete))
                {
                    rejected = FactoryRejectedBusy;
                    break;
                }
                _supplyConsumers.RemoveWhere(p => p.Store == ent.Owner);
                ent.Comp.Kind = args.Kind;
                ent.Comp.Enabled = TryRegisterSupplyConsumer(args.Generation, ent, args.Kind);
                var failure = SWGlobalWarSupplyFailure.Unavailable;
                success = ent.Comp.Enabled && TryReserveSupplyProduction(new(args.Generation, args.Request, ent,
                    actor.Faction, args.Kind, 1, args.Kind == SWGlobalWarSupplyKind.Medicine &&
                    Simulation!.SupplyPrograms[actor.Faction].Lines[args.Kind].Bacta), out _, out failure);
                if (ent.Comp.Enabled && failure != SWGlobalWarSupplyFailure.None)
                    rejected = "sw-global-war-supply-failure-" + failure.ToString().ToLowerInvariant();
                break;
            case SWGlobalWarFactoryAction.Stop when (ent.Comp.Factory || ent.Comp.Mining) && FactoryOperator(actor):
                ent.Comp.Enabled = false;
                _supplyConsumers.RemoveWhere(p => p.Store == ent.Owner);
                success = true;
                break;
            case SWGlobalWarFactoryAction.Insert:
                success = InsertFactoryItem(ent, actor, ref rejected);
                break;
            case SWGlobalWarFactoryAction.Take when args.Item is { } net:
                success = TakeFactoryItem(ent, actor, GetEntity(net), ref rejected);
                break;
            case SWGlobalWarFactoryAction.Recruit when ent.Comp.Recruitment:
                if (actor.Role != SWGlobalWarRole.Civilian)
                    rejected = "sw-global-war-factory-rejected-recruit-serving";
                else if (!_factoryIds.TryFindIdCard(actor.Body, out _))
                    rejected = "sw-global-war-factory-rejected-recruit-id";
                else if (TryComp<ActorComponent>(actor.Body, out var player))
                    success = _assignments.TryRecruitCivilian(player.PlayerSession, actor.Generation, actor.Revision);
                break;
            case SWGlobalWarFactoryAction.Materialize:
                success = MaterializeFactoryStock(ent, actor, args.Request, args.Selection, ref rejected);
                break;
            case SWGlobalWarFactoryAction.Produce:
                success = StartFactoryRecipe(ent, actor, args.Request, args.Selection, ref rejected);
                break;
            case SWGlobalWarFactoryAction.CargoWork when args.Item is { } carrier && Guid.TryParse(args.Selection, out var work):
                success = ExecuteFactoryCargoWork(actor, args.Request, work, carrier);
                rejected = "sw-global-war-supply-work-transfer-failed";
                break;
            case SWGlobalWarFactoryAction.PreviousPage:
                _factoryPages[(ent, args.Actor)] = Math.Max(0, _factoryPages.GetValueOrDefault((ent, args.Actor)) - 1);
                success = true;
                break;
            case SWGlobalWarFactoryAction.NextPage:
                _factoryPages[(ent, args.Actor)] = Math.Min(1000, _factoryPages.GetValueOrDefault((ent, args.Actor)) + 1);
                success = true;
                break;
        }
        ent.Comp.Feedback = Loc.GetString(success ? "sw-global-war-factory-success" : rejected);
        SendFactoryView(ent, args.Actor);
    }

    internal void UpdatePhysicalFactories(float frameTime)
    {
        UpdateStartingArsenals();
        TrackPhysicalFlights();
        // A reserved batch must wake its location before the usual unpaused entity query can see it.
        var query = EntityManager.AllEntityQueryEnumerator<SWGlobalWarFactoryComponent>();
        while (query.MoveNext(out var uid, out var factory))
        {
            var previous = factory.LastUpdate;
            factory.LastUpdate = _factoryTiming.CurTime;
            if (!BindFactory((uid, factory), out var owner) || !factory.Factory && !factory.Mining)
            {
                ReleaseFactoryLease(factory);
                continue;
            }
            if (factory.Factory)
                CollectFactoryInputs((uid, factory), owner);
            if (!factory.Enabled)
            {
                ReleaseFactoryLease(factory);
                continue;
            }
            var pending = GetSupplyProductionWork(factory.Generation, uid);
            var additional = _factoryRecipeWork.Values.FirstOrDefault(w => w.Generation == factory.Generation &&
                w.Store == uid && w.Owner == owner && !w.Complete);
            var mining = CanRunPhysicalMine((uid, factory), owner, out var miningWorld, out var miningCommodity);
            if (pending.Count == 0 && additional == null && !mining)
            {
                ReleaseFactoryLease(factory);
                continue;
            }
            if (factory.WorkLease == null)
            {
                if (!_hubPlanets.TryGetPhysicalLocation(uid, out var planet, out var location) ||
                    !_hubPlanets.TryAcquireLocationLease(planet, location, uid, Guid.NewGuid(),
                        "campaign-production", out var lease))
                    continue;
                factory.WorkLease = lease;
                previous = _factoryTiming.CurTime;
            }
            if (!this.IsPowered(uid, EntityManager))
                continue;
            var elapsed = previous == TimeSpan.Zero ? TimeSpan.Zero : _factoryTiming.CurTime - previous;
            if (mining)
                AdvancePhysicalMine((uid, factory), owner, miningWorld, miningCommodity, elapsed);
            if (additional != null)
                AdvanceFactoryRecipe(additional, elapsed);
            foreach (var work in pending)
            {
                if (!factory.Work.TryGetValue(work.Request.Operation, out var physical))
                    factory.Work[work.Request.Operation] = physical = new();
                if (work.Stage == SWGlobalWarSupplyStage.Reserved)
                {
                    TryBeginSupplyProduction(factory.Generation, work.Request.Operation);
                    continue;
                }
                physical.ProductiveTime += elapsed;
                if (physical.ProductiveTime < PhysicalSupplyDuration(work))
                    continue;
                if (physical.Output == null)
                {
                    physical.Output = new();
                    for (var i = 0; i < work.Quantity; i++)
                    {
                        var item = Spawn(SupplyPrototype(work.Request.Kind, work.Request.Faction, work.Request.Bacta),
                            SupplyProductionOutputCoordinates(work.Request.Operation));
                        physical.Output.Add(item);
                        if (TryComp<BatteryComponent>(item, out var battery))
                            _factoryBatteries.SetCharge((item, battery), 0);
                        if (_supplyContainers.TryGetContainer(item, "gun_magazine", out var magazine))
                            foreach (var loaded in magazine.ContainedEntities.ToArray())
                                Del(loaded);
                    }
                }
                // Machine progress and the base program's planned rate are separate constraints.
                // Finished pieces wait in the existing escrow until the plan permits their release.
                if (Simulation!.Time >= work.ReadyAt &&
                    TryCompleteSupplyProduction(factory.Generation, work.Request.Operation, physical.Output))
                    factory.Work.Remove(work.Request.Operation);
            }
        }
        if (_factoryTiming.CurTime < _factoryNextView)
            return;
        _factoryNextView = _factoryTiming.CurTime + TimeSpan.FromSeconds(1);
        foreach (var (store, actor) in _factorySubscriptions.Keys.ToArray())
            if (TryComp<SWGlobalWarFactoryComponent>(store, out var comp))
                SendFactoryView((store, comp), actor);
            else
                _factorySubscriptions.Remove((store, actor));
    }

    private static TimeSpan PhysicalSupplyDuration(SWGlobalWarSupplyProduction work)
    {
        var seconds = work.Request.Kind switch
        {
            SWGlobalWarSupplyKind.T1 or SWGlobalWarSupplyKind.Magazine or
                SWGlobalWarSupplyKind.Ration or SWGlobalWarSupplyKind.AdvancedElectronics => 30,
            SWGlobalWarSupplyKind.Medicine when work.Request.Bacta => 120,
            _ => 60,
        };
        return TimeSpan.FromSeconds(seconds * work.Request.Batches);
    }

    private void ReleaseFactoryLease(SWGlobalWarFactoryComponent factory)
    {
        if (factory.WorkLease is not { } lease)
            return;
        _hubPlanets.ReleaseLocationLease(lease);
        factory.WorkLease = null;
    }

    private bool InsertFactoryItem(Entity<SWGlobalWarFactoryComponent> ent, SWGlobalWarAssignment actor, ref string rejected)
    {
        if (!_supplyHands.TryGetActiveItem(actor.Body, out var item))
        {
            rejected = "sw-global-war-factory-rejected-no-item";
            return false;
        }
        if (!TryGetSupplyStore(actor.Generation, ent, actor.Faction, out _, out var inventory))
            return false;
        if (_supplyItems.TryGetValue(item.Value, out var registered))
        {
            if (!ent.Comp.Arsenal && ent.Comp.Factory && !FactoryOperator(actor))
            {
                rejected = FactoryRejectedOperator;
                return false;
            }
            if (registered.Generation != actor.Generation || registered.Owner != actor.Faction ||
                ent.Comp.Arsenal && !registered.Accepted || !ent.Comp.Arsenal &&
                (!ent.Comp.Factory || !FactoryOperator(actor) || registered.Market || registered.Order != null))
            {
                rejected = FactoryRejectedItem;
                return false;
            }
        }
        else if (ent.Comp.Factory && !FactoryOperator(actor))
        {
            rejected = FactoryRejectedOperator;
            return false;
        }
        else if (!ent.Comp.Factory ||
                 !Enum.GetValues<SWGlobalWarSupplyKind>().SelectMany(k => SupplyRecipe(k, false).Physical.Keys)
                     .Concat(FactoryRecipes(ent).SelectMany(r => r.Materials.Keys))
                     .Any(p => MatchesSupplyMaterial(item.Value, p)))
        {
            rejected = FactoryRejectedItem;
            return false;
        }
        if (_supplyHands.TryDropIntoContainer(actor.Body, item.Value, inventory))
            return true;
        rejected = FactoryRejectedSpace;
        return false;
    }

    private bool CanTakeFactoryItem(Entity<SWGlobalWarFactoryComponent> ent, SWGlobalWarAssignment actor, EntityUid item)
    {
        return FactoryTakeFailure(ent, actor, item) == null;
    }

    /// <summary>The localization key naming why this item cannot be taken, or null when it can.</summary>
    private string? FactoryTakeFailure(Entity<SWGlobalWarFactoryComponent> ent, SWGlobalWarAssignment actor, EntityUid item)
    {
        if (!TryGetSupplyStore(actor.Generation, ent, actor.Faction, out _, out var inventory) ||
            !inventory.Contains(item))
            return "sw-global-war-factory-rejected-gone";
        if (!_supplyItems.TryGetValue(item, out var record))
            return !ent.Comp.Factory && !ent.Comp.Mining ? FactoryRejectedItem :
                !FactoryOperator(actor) ? FactoryRejectedOperator : null;
        if (record.Generation != actor.Generation || record.Owner != actor.Faction)
            return FactoryRejectedItem;
        if (!ent.Comp.Arsenal)
            return !FactoryOperator(actor) ? FactoryRejectedOperator :
                record.Market || record.Order != null ? FactoryRejectedItem : null;
        if (!record.Accepted)
            return FactoryRejectedItem;
        if (actor.Role is not (SWGlobalWarRole.Soldier or SWGlobalWarRole.Pilot or
            SWGlobalWarRole.JuniorOfficer or SWGlobalWarRole.SeniorOfficer or SWGlobalWarRole.Intelligence))
            return "sw-global-war-factory-rejected-arsenal-role";
        var limit = record.Kind switch
        {
            SWGlobalWarSupplyKind.Magazine => 3,
            SWGlobalWarSupplyKind.Ration or SWGlobalWarSupplyKind.Medicine => 2,
            SWGlobalWarSupplyKind.AdvancedElectronics => 0,
            _ => 1,
        };
        var allowed = !_factoryIssued.TryGetValue((actor.Id, record.Kind), out var issued) ? limit > 0 :
            issued.Contains(record.Unit) || issued.Count < limit;
        return allowed ? null : "sw-global-war-factory-rejected-issue-limit";
    }

    /// <summary>Whether this service member has drawn a weapon or armour from an arsenal.</summary>
    public bool HasDrawnKit(Guid assignment)
    {
        return _factoryIssued.Any(pair => pair.Key.Assignment == assignment && pair.Value.Count > 0 &&
                                          pair.Key.Kind is SWGlobalWarSupplyKind.T1 or SWGlobalWarSupplyKind.T2 or SWGlobalWarSupplyKind.Armor);
    }

    /// <summary>Whether this arsenal still holds a weapon or armour to issue to this side.</summary>
    public bool ArsenalHasKit(Guid generation, EntityUid arsenal, SWGlobalWarFaction faction)
    {
        return TryGetSupplyStore(generation, arsenal, faction, out _, out var inventory) &&
               inventory.ContainedEntities.Any(item => _supplyItems.TryGetValue(item, out var record) &&
                   record.Accepted && record.Owner == faction &&
                   record.Kind is SWGlobalWarSupplyKind.T1 or SWGlobalWarSupplyKind.T2 or SWGlobalWarSupplyKind.Armor);
    }

    private bool TakeFactoryItem(Entity<SWGlobalWarFactoryComponent> ent, SWGlobalWarAssignment actor, EntityUid item,
        ref string rejected)
    {
        if (FactoryTakeFailure(ent, actor, item) is { } failure)
        {
            rejected = failure;
            return false;
        }
        if (!ent.Comp.Arsenal)
        {
            if (TakeFactoryOutput(ent, actor, item))
                return true;
            rejected = HasComp<ItemComponent>(item)
                ? "sw-global-war-factory-rejected-hands" : "sw-global-war-factory-rejected-platform";
            return false;
        }
        var record = _supplyItems[item];
        var key = (actor.Id, record.Kind);
        if (!_factoryIssued.TryGetValue(key, out var issued))
            _factoryIssued[key] = issued = new();
        var added = issued.Add(record.Unit);
        if (_supplyHands.TryPickupAnyHand(actor.Body, item))
            return true;
        if (added)
            issued.Remove(record.Unit);
        rejected = "sw-global-war-factory-rejected-hands";
        return false;
    }

    private void SendFactoryView(Entity<SWGlobalWarFactoryComponent> ent, EntityUid body)
    {
        if (!_factorySubscriptions.TryGetValue((ent, body), out var subscription) ||
            !_factoryUi.IsUiOpen(ent.Owner, SWGlobalWarFactoryUiKey.Key, body))
        {
            _factorySubscriptions.Remove((ent, body));
            _factoryPages.Remove((ent, body));
            return;
        }
        var valid = FactoryActor(ent, body, out var actor);
        var rows = new List<SWGlobalWarFactoryItem>();
        var remaining = 0d;
        var total = 0d;
        var products = new List<SWGlobalWarFactoryProduct>();
        var page = 0;
        var pages = 1;
        if (valid && TryGetSupplyStore(actor.Generation, ent, actor.Faction, out _, out var inventory))
        {
            var listed = ent.Comp.Arsenal
                ? inventory.ContainedEntities.GroupBy(e => Name(e)).Select(g =>
                    new SWGlobalWarFactoryItem(GetNetEntity(g.FirstOrDefault(e => CanTakeFactoryItem(ent, actor, e), g.First())),
                        g.Key, g.Any(e => CanTakeFactoryItem(ent, actor, e)),
                        MetaData(g.First()).EntityPrototype?.ID, g.Count())).ToList()
                : inventory.ContainedEntities.Select(e => new SWGlobalWarFactoryItem(GetNetEntity(e), Name(e),
                    CanTakeFactoryItem(ent, actor, e), MetaData(e).EntityPrototype?.ID)).ToList();
            pages = Math.Max(1, (listed.Count + 79) / 80);
            page = Math.Clamp(_factoryPages.GetValueOrDefault((ent, body)), 0, pages - 1);
            _factoryPages[(ent, body)] = page;
            rows = listed.Skip(page * 80).Take(80).ToList();
            if (ent.Comp.Factory && FactoryOperator(actor))
                products = FactoryProducts(ent, actor, inventory);
            foreach (var work in GetSupplyProductionWork(actor.Generation, ent))
                total += PhysicalSupplyDuration(work).TotalSeconds;
            foreach (var work in _factoryRecipeWork.Values.Where(w => w.Store == ent.Owner && !w.Complete))
                total += work.Recipe.Duration.TotalSeconds;
            foreach (var work in GetSupplyProductionWork(actor.Generation, ent))
                remaining += Math.Max(0, Math.Max((PhysicalSupplyDuration(work) -
                    (ent.Comp.Work.GetValueOrDefault(work.Request.Operation)?.ProductiveTime ?? TimeSpan.Zero)).TotalSeconds,
                    (work.ReadyAt - Simulation!.Time).TotalSeconds));
            foreach (var work in _factoryRecipeWork.Values.Where(w => w.Store == ent.Owner && !w.Complete))
                remaining += Math.Max(0, (work.Recipe.Duration - work.ProductiveTime).TotalSeconds);
        }
        var status = Loc.GetString(!valid ? "sw-global-war-factory-unavailable" :
            ent.Comp.Recruitment ? "sw-global-war-factory-recruit-status" :
            ent.Comp.Arsenal ? "sw-global-war-factory-arsenal-status" : "sw-global-war-factory-status",
            ("powered", Loc.GetString(this.IsPowered(ent, EntityManager) ? "sw-global-war-factory-yes" : "sw-global-war-factory-no")),
            ("enabled", Loc.GetString(ent.Comp.Enabled ? "sw-global-war-factory-yes" : "sw-global-war-factory-no")),
            ("seconds", (int) Math.Ceiling(remaining)));
        var detail = valid ? ent.Comp.Feedback : string.Empty;
        if (valid && ent.Comp.Mining && _supplyStores.TryGetValue(ent, out var miningStore))
            detail = (detail.Length > 0 ? detail + "\n" : string.Empty) + Loc.GetString("sw-global-war-factory-mining",
                ("remaining", _physicalMiningDeposits.GetValueOrDefault(miningStore.Planet, PhysicalMiningDeposit)));
        if (detail.Length > 0)
            status += "\n" + detail;
        _factoryUi.ServerSendUiMessage(ent.Owner, SWGlobalWarFactoryUiKey.Key,
            new SWGlobalWarFactorySnapshot(new(valid ? actor.Generation : Guid.Empty, valid ? actor.Revision : 0,
                valid, ent.Comp.Factory, ent.Comp.Recruitment, ent.Comp.Kind, status, rows,
                valid && FactoryOperator(actor) ? FactoryMaterials(ent) : new(),
                valid && FactoryOperator(actor) ? FactoryRecipes(ent).Select(r => new SWGlobalWarFactoryChoice(r.Id,
                    string.Join(", ", r.Results.Select(p => $"{_prototypes.Index<Robust.Shared.Prototypes.EntityPrototype>(p.Key).Name} ×{p.Value}")))).ToList() : new(),
                page, pages, FactoryKinds(ent), ent.Comp.Mining, ent.Comp.Arsenal,
                this.IsPowered(ent, EntityManager), ent.Comp.Enabled, (int) Math.Ceiling(remaining), (int) Math.Ceiling(total),
                detail, products, valid ? FactoryCargoWork(ent, actor) : null), subscription, GetNetEntity(body)), body);
    }
}
