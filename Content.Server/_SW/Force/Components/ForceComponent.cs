using Robust.Shared.Audio;

namespace Content.Server._SW.Force.Components;

[RegisterComponent]
public partial class ForceComponent : Component
{
    public List<string> AbilitiesPrototypes =
    [
        "ActionJediDash"
    ];

    [DataField("exp")]
    public int Experience;

    [DataField("addexpcritordead")]
    public int AddExperienceCriticalOrDead = 10;

    [DataField("exptonewlevel")]
    public int ExperienceToNewLevel = 100;

    // Dash
    [DataField("DashSound")]
    public SoundSpecifier DashSound = new SoundPathSpecifier("/Audio/_SW/Abilities/dash.ogg");

    [DataField("DashDistance")]
    public int DashDistance = 2;

    public string? DashEffectStart = "DashEffectStart";

    public string? DashEffectEnd = "DashEffectEnd";
}
