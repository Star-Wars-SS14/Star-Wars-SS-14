using Robust.Shared.Audio;

namespace Content.Server._SW.Force.Components;

[RegisterComponent]
public partial class ForceComponent : Component
{
    public List<string> AbilitiesPrototypes =
    [
        "ActionJediDash"
    ];

    // Dash
    [DataField("DashSound")]
    public SoundSpecifier DashSound = new SoundPathSpecifier("/Audio/_SW/Abilities/dash.ogg");

    [DataField("DashDistance")]
    public int DashDistance = 2;

    public string? DashEffectStart = "DashEffectStart";

    public string? DashEffectEnd = "DashEffectEnd";
}
