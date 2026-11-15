using Content.Server._SW;
using Content.Server.Stunnable;
using Content.Shared._SW.Items;
using Content.Shared.Clothing.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.DeviceLinking.Events;
using Robust.Shared.Audio.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using System;

namespace Content.Server._SW.Items;

/// <summary>
///     Applies the shock to the wearer when the collar receives its device link signal.
/// </summary>
public sealed class SWElectricCollarSignalSystem : EntitySystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly StunSystem _stun = default!;
    [Dependency] private readonly SWElectrifiedEffectSystem _electrified = default!;
    [Dependency] private readonly SWLightningSparksSystem _sparks = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWElectricCollarComponent, SignalReceivedEvent>(OnSignalReceived);
    }

    private void OnSignalReceived(Entity<SWElectricCollarComponent> ent, ref SignalReceivedEvent args)
    {
        if (args.Port != ent.Comp.Port)
            return;

        // Only shock while worn; the wearer is the item's parent when equipped.
        if (TryComp<ClothingComponent>(ent, out var clothing) && clothing.InSlot is null)
            return;

        var wearer = Transform(ent).ParentUid;
        if (!Exists(wearer))
            return;

        var seconds = (float) ent.Comp.StunTime.TotalSeconds;

        // Victim-side Sith lightning effects (no beam).
        _electrified.AddElectrifiedEffect(wearer, seconds);
        _sparks.AddLightningSparks(wearer, seconds);
        Spawn("EffectSparks", Transform(wearer).Coordinates);
        _audio.PlayPvs(ent.Comp.ShockSound, wearer);

        var damage = ent.Comp.Damage.Empty
            ? new DamageSpecifier(_proto.Index<DamageTypePrototype>("Shock"), 5)
            : ent.Comp.Damage;
        _damageable.TryChangeDamage(wearer, damage, true);

        _stun.TryAddStunDuration(wearer, ent.Comp.StunTime, false);
        _stun.TryKnockdown(wearer, ent.Comp.StunTime, true);
    }
}