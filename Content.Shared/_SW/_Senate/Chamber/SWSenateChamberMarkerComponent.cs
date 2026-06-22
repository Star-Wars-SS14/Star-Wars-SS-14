using System.Numerics;

namespace Content.Shared._SW._Senate.Chamber;

[RegisterComponent]
public sealed partial class SWSenateChamberMarkerComponent : Component
{
    [DataField]
    public Vector2 Zoom = new(2.5f, 2.5f);

    [DataField]
    public float PvsScale = 2.5f;
}
