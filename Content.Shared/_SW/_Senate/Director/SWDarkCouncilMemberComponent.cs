using Robust.Shared.GameStates;
using Content.Shared._SW._Senate.Empire;

namespace Content.Shared._SW._Senate.Director;

[RegisterComponent, NetworkedComponent]
public sealed partial class SWDarkCouncilMemberComponent : Component
{
    [DataField] public SWEmpireCouncilSphere Sphere = SWEmpireCouncilSphere.None;
}
