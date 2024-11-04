using System.Runtime.InteropServices.JavaScript;
using Robust.Shared.Audio;

namespace Content.Server._SW.Force.Components;

[RegisterComponent]
public sealed partial class LightForceComponent : ForceComponent
{
    public List<string> AbilitiesPrototypes =
    [
        "ActionJediDash",
    ];
}
