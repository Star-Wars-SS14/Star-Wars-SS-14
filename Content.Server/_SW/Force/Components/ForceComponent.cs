using Content.Shared.Store;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

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

    [DataField("currencyproto")]
    public ProtoId<CurrencyPrototype> MasteryCurrencyProto = "MasteryExperience";

    [DataField("addexpcritordead")]
    public int AddExperienceCriticalOrDead = 10;

    // Dash
    [DataField("DashSound")]
    public SoundSpecifier DashSound = new SoundPathSpecifier("/Audio/_SW/Abilities/dash.ogg");

    [DataField("DashDistance")]
    public int DashDistance = 2;

    public string? DashEffectStart = "DashEffectStart";

    public string? DashEffectEnd = "DashEffectEnd";
}
