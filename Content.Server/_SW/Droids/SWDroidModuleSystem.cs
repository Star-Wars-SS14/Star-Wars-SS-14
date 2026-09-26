using Content.Server.Popups;
using Content.Server._SW.Targeting;
using Content.Server.Access.Components;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared._SW.Droids;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Containers;
using Content.Shared.Interaction;
using Content.Shared.Roles;
using Content.Shared.Silicons.Borgs.Components;
using Content.Shared.Wires;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._SW.Droids;

public sealed class SWDroidModuleSystem : EntitySystem
{
    [Dependency] private readonly AccessReaderSystem _accessReader = default!;
    [Dependency] private readonly SharedAccessSystem _access = default!;
    [Dependency] private readonly HTNSystem _htn = default!;
    [Dependency] private readonly NPCSteeringSystem _steering = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly SWTargetingConfigTargetsSystem _targeting = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BorgChassisComponent, InteractUsingEvent>(OnBeforeBorgModuleInsert);
        SubscribeLocalEvent<SWDroidCommandableComponent, MapInitEvent>(OnDroidMapInit, after: [typeof(ContainerFillSystem)]);

        SubscribeLocalEvent<SWDroidFactionModuleComponent, EntGotInsertedIntoContainerMessage>(OnFactionModuleInserted);
        SubscribeLocalEvent<SWDroidFactionModuleComponent, EntGotRemovedFromContainerMessage>(OnFactionModuleRemoved);
        SubscribeLocalEvent<SWDroidAICoreModuleComponent, EntGotRemovedFromContainerMessage>(OnAICoreRemoved);
    }

    private void OnDroidMapInit(Entity<SWDroidCommandableComponent> droid, ref MapInitEvent args)
    {
        if (!TryComp<BorgChassisComponent>(droid, out var chassis))
            return;

        RefreshFactionFromModules((droid.Owner, chassis));
    }

    private void OnBeforeBorgModuleInsert(Entity<BorgChassisComponent> chassis, ref InteractUsingEvent args)
    {
        if (args.Handled ||
            !TryComp<BorgModuleComponent>(args.Used, out _) ||
            !TryComp<SWDroidModuleLimitsComponent>(chassis, out var limits) ||
            !TryComp<SWDroidModuleCategoryComponent>(args.Used, out var category))
        {
            return;
        }

        if (TryComp<WiresPanelComponent>(chassis, out var panel) && !panel.Open)
            return;

        if (CanInsertModuleCategory(chassis, limits, category.Category))
            return;

        _popup.PopupEntity(Loc.GetString("sw-droid-module-category-limit"), chassis, args.User);
        args.Handled = true;
    }

    private bool CanInsertModuleCategory(Entity<BorgChassisComponent> chassis, SWDroidModuleLimitsComponent limits, string category)
    {
        if (!limits.Limits.TryGetValue(category, out var limit))
            return true;

        var count = 0;
        foreach (var contained in chassis.Comp.ModuleContainer.ContainedEntities)
        {
            if (TryComp<SWDroidModuleCategoryComponent>(contained, out var containedCategory) &&
                containedCategory.Category == category)
            {
                count++;
            }
        }

        return count < limit;
    }

    private void OnFactionModuleInserted(Entity<SWDroidFactionModuleComponent> module, ref EntGotInsertedIntoContainerMessage args)
    {
        if (!TryGetDroidFromModuleContainer(args.Container, out var droid))
            return;

        ApplyFactionModule(droid, module.Comp);
    }

    private void OnFactionModuleRemoved(Entity<SWDroidFactionModuleComponent> module, ref EntGotRemovedFromContainerMessage args)
    {
        if (!TryGetDroidFromModuleContainer(args.Container, out var droid))
            return;

        RefreshFactionFromModules(droid);
    }

    private void OnAICoreRemoved(Entity<SWDroidAICoreModuleComponent> module, ref EntGotRemovedFromContainerMessage args)
    {
        if (!TryGetDroidFromModuleContainer(args.Container, out var droid))
            return;

        DisableDroidAI(droid.Owner);
    }

    private bool TryGetDroidFromModuleContainer(BaseContainer container, out Entity<BorgChassisComponent> droid)
    {
        droid = default;
        var owner = container.Owner;

        if (!TryComp<BorgChassisComponent>(owner, out var chassis) ||
            container != chassis.ModuleContainer)
        {
            return false;
        }

        droid = (owner, chassis);
        return true;
    }

    public void RefreshFactionFromModules(Entity<BorgChassisComponent> droid)
    {
        foreach (var module in droid.Comp.ModuleContainer.ContainedEntities)
        {
            if (!TryComp<SWDroidFactionModuleComponent>(module, out var factionModule))
                continue;

            ApplyFactionModule(droid, factionModule);
            return;
        }

        ClearFactionModule(droid);
    }

    private void ApplyFactionModule(Entity<BorgChassisComponent> droid, SWDroidFactionModuleComponent module)
    {
        _targeting.UnregisterTargetFromAll(droid);

        if (TryComp<SWDroidCommandableComponent>(droid, out var commandable))
        {
            commandable.Faction = module.Faction;
            commandable.ActiveCommander = null;
            commandable.ActiveCommanderRank = 0;
            Dirty(droid, commandable);
        }

        ApplyFactionAccess(droid.Owner, module);

        if (TryComp<AccessReaderComponent>(droid, out var accessReader))
        {
            _accessReader.TrySetAccesses((droid.Owner, accessReader), module.LockAccess);
        }
    }

    private void ClearFactionModule(Entity<BorgChassisComponent> droid)
    {
        _targeting.UnregisterTargetFromAll(droid);

        if (TryComp<SWDroidCommandableComponent>(droid, out var commandable))
        {
            commandable.Faction = string.Empty;
            commandable.ActiveCommander = null;
            commandable.ActiveCommanderRank = 0;
            Dirty(droid, commandable);
        }

        ClearFactionAccess(droid.Owner);

        if (TryComp<AccessReaderComponent>(droid, out var accessReader))
        {
            _accessReader.TryClearAccesses((droid.Owner, accessReader));
        }
    }

    private void ApplyFactionAccess(EntityUid droid, SWDroidFactionModuleComponent module)
    {
        var id = EnsureComp<PresetIdCardComponent>(droid);
        id.JobName = module.Job;

        if (module.Job == null)
        {
            ClearIdAccess(droid);
            return;
        }

        if (!_prototype.TryIndex(module.Job.Value, out JobPrototype? job))
        {
            Log.Error($"Invalid job id ({module.Job.Value}) for droid faction chip.");
            ClearIdAccess(droid);
            return;
        }

        var access = EnsureComp<AccessComponent>(droid);
        _access.SetAccessToJob(droid, job, false, access);
    }

    private void ClearFactionAccess(EntityUid droid)
    {
        RemCompDeferred<PresetIdCardComponent>(droid);
        ClearIdAccess(droid);
    }

    private void ClearIdAccess(EntityUid droid)
    {
        RemCompDeferred<AccessComponent>(droid);
    }

    private void DisableDroidAI(EntityUid droid)
    {
        if (TryComp<SWDroidCommandableComponent>(droid, out var commandable))
        {
            if (commandable.ActiveCommander != null &&
                TryComp<SWDroidCommanderComponent>(commandable.ActiveCommander.Value, out var commander))
            {
                commander.Droids.Remove(droid);
            }

            commandable.ActiveCommander = null;
            commandable.ActiveCommanderRank = 0;
            Dirty(droid, commandable);
        }

        _targeting.UnregisterTargetFromAll(droid);
        _steering.Unregister(droid);

        if (!TryComp<HTNComponent>(droid, out var htn))
            return;

        if (htn.Plan != null)
            _htn.ShutdownPlan(htn);

        htn.Blackboard.Remove<EntityCoordinates>(NPCBlackboard.FollowTarget);
        htn.Blackboard.Remove<EntityCoordinates>("FollowIdleTarget");
        htn.Blackboard.Remove<float>("IdleTime");
        _htn.Replan(htn);
    }
}
