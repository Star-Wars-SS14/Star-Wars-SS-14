using Robust.Shared.Audio;

namespace Content.Shared._SW.Vehicle;

[RegisterComponent]
public sealed partial class SWVehicleMovementSoundComponent : Component
{
    [DataField(required: true), ViewVariables(VVAccess.ReadWrite)]
    public SoundSpecifier Sound = default!;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float MinimumSpeed = 0.3f;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float MinimumAngularVelocity = 0.1f;

    [ViewVariables(VVAccess.ReadWrite)]
    public EntityUid? Stream;
}
