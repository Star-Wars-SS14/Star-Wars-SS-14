using Content.Shared.Interaction;
using Content.Shared.Interaction.Components;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;
using Robust.Shared.Player;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW.Vehicle;
public sealed class SWRedirectMovementSystem : EntitySystem
{
    [Dependency] private readonly SharedMoverController _mover = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movementSpeedModifierSystem = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<SWRedirectMovementComponent, SWUserEnterVehicleEvent>(OnEnter);
        SubscribeLocalEvent<SWRedirectMovementComponent, SWUserExitVehicleEvent>(OnExit);
    }

    private void OnEnter(Entity<SWRedirectMovementComponent> ent, ref SWUserEnterVehicleEvent args)
    {
        ISawmill _s = Logger.GetSawmill("asd");
        _s.Fatal("SADFG");

        EnsureComp<RelayInputMoverComponent>(ent);
        _mover.SetRelay(ent, args.Vehicle);

        var comp2 = EnsureComp<InteractionRelayComponent>(ent);
        _interaction.SetRelay(ent, args.Vehicle, comp2);

        _movementSpeedModifierSystem.RefreshWeightlessModifiers(args.Vehicle);
        _movementSpeedModifierSystem.RefreshWeightlessModifiers(ent);
    }


    private void OnExit(Entity<SWRedirectMovementComponent> ent, ref SWUserExitVehicleEvent args)
    {
        RemComp<RelayInputMoverComponent>(ent);
        RemComp<InteractionRelayComponent>(ent);
        _movementSpeedModifierSystem.RefreshWeightlessModifiers(args.Vehicle);
        _movementSpeedModifierSystem.RefreshWeightlessModifiers(ent);
    }
}


