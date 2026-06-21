using Content.Shared._SW;
using Content.Shared.Damage;

namespace Content.Shared._SW.Vehicle;

public sealed class SWInVehicleDamageProtectionSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SWInVehicleDamageProtectionComponent, SWAfterAllModifiersAppliedDamageModifyEvent>(OnDamageModify);
    }

    private void OnDamageModify(Entity<SWInVehicleDamageProtectionComponent> ent, ref SWAfterAllModifiersAppliedDamageModifyEvent args)
    {
        if (!HasComp<SWInVehicleComponent>(ent) || !args.Damage.AnyPositive())
            return;

        args.Damage = DamageSpecifier.GetNegative(args.Damage) + DamageSpecifier.GetPositive(args.Damage) * ent.Comp.DamageMultiplier;
    }
}
