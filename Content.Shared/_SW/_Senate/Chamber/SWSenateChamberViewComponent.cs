using System.Numerics;
using Robust.Shared.GameStates;

namespace Content.Shared._SW._Senate.Chamber;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SWSenateChamberViewComponent : Component
{
    [AutoNetworkedField]
    public bool Applied;

    [AutoNetworkedField]
    public Vector2 Offset;

    [AutoNetworkedField]
    public float PvsScale = 1f;

    public Vector2? PreviousZoom;
    public Vector2? PreviousMaxZoom;
}
