using System.Runtime.InteropServices.JavaScript;
using Robust.Shared.Audio;

namespace Content.Server._SW.Force.Components;

[RegisterComponent]
public sealed partial class LightForceComponent : ForceComponent
{
    public List<string> AbilitiesLightPrototypes = [];

    [DataField]
    public JediMasteryLevel Mastery = JediMasteryLevel.Padawan;
}

public enum JediMasteryLevel : byte
{
    Padawan = 0,
    JediMaster = 1,
    JediGrandMaster = 2,
}
