namespace Content.Server._SW.Force.Components;

[RegisterComponent]
public sealed partial class SaberComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? Owner;
}
