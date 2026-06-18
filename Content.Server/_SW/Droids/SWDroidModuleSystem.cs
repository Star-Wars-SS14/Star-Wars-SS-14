using Content.Server.Popups;
using Content.Shared._SW.Droids;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Interaction;
using Content.Shared.Silicons.Borgs;
using Content.Shared.Silicons.Borgs.Components;
using Robust.Shared.Containers;

namespace Content.Server._SW.Droids;

public sealed class SWDroidModuleSystem : EntitySystem
{
    [Dependency] private readonly AccessReaderSystem _accessReader = default!;
    [Dependency] private readonly PopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BorgChassisComponent, AfterInteractUsingEvent>(
            OnBeforeBorgModuleInsert,
            before: [typeof(SharedBorgSystem)]);

        SubscribeLocalEvent<SWDroidFactionModuleComponent, EntGotInsertedIntoContainerMessage>(OnFactionModuleInserted);
        SubscribeLocalEvent<SWDroidFactionModuleComponent, EntGotRemovedFromContainerMessage>(OnFactionModuleRemoved);
    }

    private void OnBeforeBorgModuleInsert(Entity<BorgChassisComponent> chassis, ref AfterInteractUsingEvent args)
    {
        if (!args.CanReach ||
            args.Handled ||
            !TryComp<BorgModuleComponent>(args.Used, out _) ||
            !TryComp<SWDroidModuleLimitsComponent>(chassis, out var limits) ||
            !TryComp<SWDroidModuleCategoryComponent>(args.Used, out var category))
        {
            return;
        }

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

    private void RefreshFactionFromModules(Entity<BorgChassisComponent> droid)
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
        if (TryComp<SWDroidCommandableComponent>(droid, out var commandable))
        {
            commandable.Faction = module.Faction;
            commandable.ActiveCommander = null;
            commandable.ActiveCommanderRank = 0;
            Dirty(droid, commandable);
        }

        if (TryComp<AccessReaderComponent>(droid, out var accessReader))
        {
            _accessReader.TrySetAccesses((droid.Owner, accessReader), module.LockAccess);
        }
    }

    private void ClearFactionModule(Entity<BorgChassisComponent> droid)
    {
        if (TryComp<SWDroidCommandableComponent>(droid, out var commandable))
        {
            commandable.Faction = string.Empty;
            commandable.ActiveCommander = null;
            commandable.ActiveCommanderRank = 0;
            Dirty(droid, commandable);
        }

        if (TryComp<AccessReaderComponent>(droid, out var accessReader))
        {
            _accessReader.TryClearAccesses((droid.Owner, accessReader));
        }
    }
}
