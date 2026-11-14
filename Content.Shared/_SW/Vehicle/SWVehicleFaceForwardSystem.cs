namespace Content.Shared._SW.Vehicle;

public sealed class SWVehicleFaceForwardSystem : EntitySystem
{
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<SWVehicleFaceForwardComponent, SWInVehicleComponent, TransformComponent>();
        while (query.MoveNext(out _, out var faceForward, out var inVehicle, out var xform))
        {
            // THIS LINE IS CRITICALLY IMPORTANT. It prevents the system from breaking our turret.
            if (HasComp<SWTurretAimComponent>(inVehicle.Vehicle))
                continue;

            FaceVehicleForward(faceForward, inVehicle, xform);
        }
    }

    private void FaceVehicleForward(
        SWVehicleFaceForwardComponent faceForward,
        SWInVehicleComponent inVehicle,
        TransformComponent xform)
    {
        if (!TryComp<TransformComponent>(inVehicle.Vehicle, out var vehicleXform))
            return;

        var vehicleRotation = _transform.GetWorldRotation(vehicleXform);
        _transform.SetWorldRotation(xform, vehicleRotation + faceForward.RotationOffset);
    }
}