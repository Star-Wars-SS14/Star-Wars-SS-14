using System;
using Content.Shared._SW.Vehicle;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;

namespace Content.Server._SW.Vehicle;

public sealed class SWTurretAimSystem : EntitySystem
{
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<SWTurretAimEvent>(OnAim);
    }

    private void OnAim(SWTurretAimEvent ev, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } gunner)
            return;

        if (!TryComp<SWInVehicleComponent>(gunner, out var inVehicle))
            return;

        var turretUid = inVehicle.Vehicle;

        if (!TryComp<SWTurretAimComponent>(turretUid, out var aim))
            return;

        aim.TargetAngle = new Angle(ev.Angle);
        Dirty(turretUid, aim);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<SWInVehicleComponent, TransformComponent>();
        while (query.MoveNext(out var gunnerUid, out var inVehicle, out var gunnerXform))
        {
            var turretUid = inVehicle.Vehicle;

            if (!TryComp<SWTurretAimComponent>(turretUid, out var aim))
                continue;

            if (!TryComp<TransformComponent>(turretUid, out var turretXform))
                continue;

            // Плавный доворот к цели по кратчайшей дуге, с ограничением скорости.
            var current = _transform.GetWorldRotation(turretXform).Theta;
            var target = aim.TargetAngle.Theta;

            var delta = target - current;
            while (delta > Math.PI) delta -= 2 * Math.PI;
            while (delta < -Math.PI) delta += 2 * Math.PI;

            var maxStep = aim.RotationSpeed * (Math.PI / 180.0) * frameTime;
            var step = Math.Clamp(delta, -maxStep, maxStep);

            var newRotation = new Angle(current + step);
            _transform.SetWorldRotation(turretXform, newRotation);

            if (TryComp<PhysicsComponent>(turretUid, out var physics))
                _physics.SetAngularVelocity(turretUid, 0f, body: physics);

            // The gunner follows the current (already smoothed) rotation of the turret.
            _transform.SetWorldRotation(gunnerXform, newRotation + aim.GunnerOffset);
        }
    }
}