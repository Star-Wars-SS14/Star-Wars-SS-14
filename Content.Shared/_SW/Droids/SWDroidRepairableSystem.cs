using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Repairable;
using Content.Shared.Stacks;
using Content.Shared.Tools.Systems;
using Robust.Shared.Serialization;

namespace Content.Shared._SW.Droids;

public sealed class SWDroidRepairableSystem : EntitySystem
{
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedStackSystem _stack = default!;
    [Dependency] private readonly SharedToolSystem _tool = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SWDroidRepairableComponent, InteractUsingEvent>(OnInteractUsing, before: [typeof(RepairableSystem)]);
        SubscribeLocalEvent<SWDroidRepairableComponent, SWDroidRepairDoAfterEvent>(OnRepairDoAfter);
    }

    private void OnInteractUsing(Entity<SWDroidRepairableComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled ||
            !_tool.HasQuality(args.Used, ent.Comp.QualityNeeded) ||
            !TryComp<DamageableComponent>(ent.Owner, out var damageable) ||
            damageable.TotalDamage <= 0)
        {
            return;
        }

        args.Handled = true;

        if (!TryFindMaterialStack(args.User, ent.Comp, out _))
        {
            _popup.PopupClient(Loc.GetString("sw-droid-repair-missing-steel"), ent.Owner, args.User);
            return;
        }

        _tool.UseTool(
            args.Used,
            args.User,
            ent.Owner,
            ent.Comp.DoAfterDelay,
            ent.Comp.QualityNeeded,
            new SWDroidRepairDoAfterEvent(),
            ent.Comp.FuelCost);
    }

    private void OnRepairDoAfter(Entity<SWDroidRepairableComponent> ent, ref SWDroidRepairDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled)
            return;

        if (!TryComp<DamageableComponent>(ent.Owner, out var damageable) ||
            damageable.TotalDamage <= 0)
        {
            return;
        }

        if (!TryFindMaterialStack(args.User, ent.Comp, out var material) ||
            !_stack.TryUse(material, ent.Comp.MaterialAmount))
        {
            _popup.PopupClient(Loc.GetString("sw-droid-repair-missing-steel"), ent.Owner, args.User);
            args.Handled = true;
            return;
        }

        _damageable.HealEvenly(ent.Owner, ent.Comp.DamageValue, origin: args.User);

        args.Repeat = ent.Comp.AutoDoAfter &&
                      damageable.TotalDamage > 0 &&
                      TryFindMaterialStack(args.User, ent.Comp, out _);
        args.Args.Event.Repeat = args.Repeat;
        args.Handled = true;

        if (args.Repeat)
            return;

        var tool = args.Used ?? args.User;
        _popup.PopupClient(Loc.GetString("comp-repairable-repair", ("target", ent.Owner), ("tool", tool)), ent.Owner, args.User);
    }

    private bool TryFindMaterialStack(
        EntityUid user,
        SWDroidRepairableComponent component,
        out Entity<StackComponent?> material)
    {
        foreach (var held in _hands.EnumerateHeld(user))
        {
            if (!TryComp<StackComponent>(held, out var stack) ||
                stack.StackTypeId != component.Material ||
                stack.Count < component.MaterialAmount)
            {
                continue;
            }

            material = (held, stack);
            return true;
        }

        material = default;
        return false;
    }
}

[Serializable, NetSerializable]
public sealed partial class SWDroidRepairDoAfterEvent : SimpleDoAfterEvent;
