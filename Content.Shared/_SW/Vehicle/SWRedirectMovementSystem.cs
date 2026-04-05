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

    public override void Initialize()
    {
        SubscribeLocalEvent<SWRedirectMovementComponent, SWUserEnterVehicleEvent>(OnEnter);
        SubscribeLocalEvent<SWRedirectMovementComponent, SWUserExitVehicleEvent>(OnExit);
    }

    private void OnEnter(Entity<SWRedirectMovementComponent> ent, ref SWUserEnterVehicleEvent args)
    {
        ISawmill _s = Logger.GetSawmill("asd");
        _s.Fatal("SADFG");
        _mover.SetRelay(ent, args.Vehicle);
        _interaction.SetRelay(ent, args.Vehicle);
    }


    private void OnExit(Entity<SWRedirectMovementComponent> ent, ref SWUserExitVehicleEvent args)
    {
        RemComp<RelayInputMoverComponent>(ent);
        RemComp<InteractionRelayComponent>(ent);
    }
}


