using System.Numerics;

namespace Content.Shared._SW.Vehicle;

[RegisterComponent]
public sealed partial class SWInVehicleZoomComponent : Component
{
    /// <summary>
    /// Zoom applied while this entity is occupying a vehicle role.
    /// Higher values zoom the camera out.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public Vector2 Zoom = new(1.5f, 1.5f);

    /// <summary>
    /// Server PVS scale applied while the zoom is active.
    /// This should usually match the largest zoom axis so the client receives entities it can see.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float PvsScale = 1.5f;

    [ViewVariables]
    public Vector2? PreviousZoom;

    [ViewVariables]
    public Vector2? PreviousMaxZoom;

    [ViewVariables]
    public bool Applied;
}
