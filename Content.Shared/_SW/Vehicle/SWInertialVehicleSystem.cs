using System.Numerics;
using Content.Shared.Friction;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Controllers;
using Robust.Shared.Physics.Systems;

namespace Content.Shared._SW.Vehicle;

public sealed partial class SWInertialVehicleSystem : VirtualController
{
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();
        UpdatesBefore.Add(typeof(TileFrictionController));
    }

    public override void UpdateBeforeSolve(bool prediction, float frameTime)
    {
        base.UpdateBeforeSolve(prediction, frameTime);

        var query = EntityQueryEnumerator<SWInertialVehicleComponent, InputMoverComponent, PhysicsComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var vehicle, out var mover, out var physics, out var xform))
        {
            UpdateVehicle(uid, vehicle, mover, physics, xform, frameTime);
        }
    }

    private void UpdateVehicle(
        EntityUid uid,
        SWInertialVehicleComponent vehicle,
        InputMoverComponent mover,
        PhysicsComponent physics,
        TransformComponent xform,
        float frameTime)
    {
        if (!mover.CanMove)
            return;

        var forward = vehicle.ForwardVector == Vector2.Zero
            ? Vector2.UnitY
            : vehicle.ForwardVector.Normalized();
        forward = _transform.GetWorldRotation(xform).RotateVec(forward).Normalized();

        var right = new Vector2(forward.Y, -forward.X);
        var velocity = physics.LinearVelocity;
        var forwardSpeed = Vector2.Dot(velocity, forward);
        var sidewaysSpeed = Vector2.Dot(velocity, right);

        var throttle = GetThrottle(mover.HeldMoveButtons);
        var steering = GetSteering(mover.HeldMoveButtons);

        _physics.SetLinearDamping(uid, physics, 0f);
        _physics.SetAngularDamping(uid, physics, 0f);

        if (!MathHelper.CloseTo(throttle, 0f))
        {
            var accel = GetAcceleration(throttle, forwardSpeed, vehicle);
            forwardSpeed += throttle * accel * frameTime;
            forwardSpeed = Math.Clamp(forwardSpeed, -vehicle.MaxReverseSpeed, vehicle.MaxForwardSpeed);
        }
        else
        {
            forwardSpeed = MoveTowards(forwardSpeed, 0f, vehicle.RollingFriction * frameTime);
        }

        sidewaysSpeed = MoveTowards(sidewaysSpeed, 0f, vehicle.SidewaysFriction * frameTime);
        velocity = forward * forwardSpeed + right * sidewaysSpeed;

        var angularVelocity = physics.AngularVelocity;
        if (!MathHelper.CloseTo(steering, 0f))
        {
            var speedFactor = vehicle.CanTurnInPlace
                ? 1f
                : Math.Clamp(MathF.Abs(forwardSpeed) / MathF.Max(vehicle.TurnSpeedReference, 0.001f), 0f, 1f);

            angularVelocity += -steering * vehicle.TurnAcceleration * speedFactor * frameTime;
            angularVelocity = Math.Clamp(angularVelocity, -vehicle.MaxAngularVelocity, vehicle.MaxAngularVelocity);
        }
        else
        {
            angularVelocity = MoveTowards(angularVelocity, 0f, vehicle.AngularDamping * frameTime);
        }

        _physics.SetLinearVelocity(uid, velocity, body: physics);
        _physics.SetAngularVelocity(uid, angularVelocity, body: physics);

        mover.WishDir = forward * forwardSpeed;
    }

    private static float GetThrottle(MoveButtons buttons)
    {
        var throttle = 0f;

        if ((buttons & MoveButtons.Up) != 0)
            throttle += 1f;

        if ((buttons & MoveButtons.Down) != 0)
            throttle -= 1f;

        return throttle;
    }

    private static float GetSteering(MoveButtons buttons)
    {
        var steering = 0f;

        if ((buttons & MoveButtons.Left) != 0)
            steering -= 1f;

        if ((buttons & MoveButtons.Right) != 0)
            steering += 1f;

        return steering;
    }

    private static float GetAcceleration(float throttle, float forwardSpeed, SWInertialVehicleComponent vehicle)
    {
        if (MathF.Sign(throttle) != MathF.Sign(forwardSpeed) && !MathHelper.CloseTo(forwardSpeed, 0f))
            return vehicle.BrakeAcceleration;

        return throttle > 0f
            ? vehicle.Acceleration
            : vehicle.ReverseAcceleration;
    }

    private static float MoveTowards(float current, float target, float maxDelta)
    {
        if (MathF.Abs(target - current) <= maxDelta)
            return target;

        return current + MathF.Sign(target - current) * maxDelta;
    }
}
