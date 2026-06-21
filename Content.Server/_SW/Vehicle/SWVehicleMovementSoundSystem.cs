using Content.Shared._SW.Vehicle;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Physics.Components;

namespace Content.Server._SW.Vehicle;

public sealed class SWVehicleMovementSoundSystem : EntitySystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SWVehicleMovementSoundComponent, ComponentShutdown>(OnShutdown);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<SWVehicleMovementSoundComponent, PhysicsComponent, InputMoverComponent>();
        while (query.MoveNext(out var uid, out var movementSound, out var physics, out var mover))
        {
            if (IsControlledMovement(movementSound, physics, mover))
                StartSound(uid, movementSound);
            else
                StopSound(movementSound);
        }
    }

    private static bool IsControlledMovement(
        SWVehicleMovementSoundComponent movementSound,
        PhysicsComponent physics,
        InputMoverComponent mover)
    {
        var hasMovementInput = (SharedMoverController.GetNormalizedMovement(mover.HeldMoveButtons) & MoveButtons.AnyDirection) != MoveButtons.None;
        if (!mover.CanMove || !hasMovementInput)
            return false;

        return physics.LinearVelocity.LengthSquared() >= movementSound.MinimumSpeed * movementSound.MinimumSpeed ||
               MathF.Abs(physics.AngularVelocity) >= movementSound.MinimumAngularVelocity;
    }

    private void StartSound(EntityUid uid, SWVehicleMovementSoundComponent movementSound)
    {
        if (movementSound.Stream != null)
            return;

        var audioParams = movementSound.Sound.Params.WithLoop(true);

        movementSound.Stream = _audio.PlayPvs(movementSound.Sound, uid, audioParams)?.Entity;
    }

    private void StopSound(SWVehicleMovementSoundComponent movementSound)
    {
        movementSound.Stream = _audio.Stop(movementSound.Stream);
    }

    private void OnShutdown(Entity<SWVehicleMovementSoundComponent> ent, ref ComponentShutdown args)
    {
        StopSound(ent.Comp);
    }
}
