using Content.Shared._SW;
using Robust.Shared.Audio;

namespace Content.Server._SW.Reinforcements.Components;

[RegisterComponent]
public sealed partial class SWReinforcementsCardComponent : Component
{
    [DataField("fraction")]
    public StarWarsFractionType FractionType = StarWarsFractionType.Empire;

    [DataField]
    public SoundSpecifier ConsoleSoundActivated = new SoundPathSpecifier("/Audio/Misc/siren.ogg");

    [DataField]
    public SoundSpecifier ConsoleSoundDeActivated = new SoundPathSpecifier("/Audio/Misc/notice2.ogg");
}
