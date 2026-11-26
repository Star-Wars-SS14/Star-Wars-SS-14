using Content.Shared._SW._Senate;

namespace Content.Shared._SW._Senate.Chamber;

[RegisterComponent]
public sealed partial class SWSenateMicComponent : Component
{
    [DataField]
    public bool Priority;

    [DataField]
    public bool SpeakerToggle;

    [DataField]
    public SWSenatePosition RequiredPosition = SWSenatePosition.Senator;
}
