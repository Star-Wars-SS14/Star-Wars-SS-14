namespace Content.Server._SW.Force.Components;

[RegisterComponent]
public sealed partial class SaberComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? Owner;

    [DataField, AutoNetworkedField]
    public EntityUid? CallSaberActionEntity;

    [DataField, AutoNetworkedField]
    public EntityUid? PrivateSaberActionEntity;

    public string CallSaberAction = "CallSaberAction";
    public string PrivateSaberAction = "ActionSaberPrivate";

    [DataField, AutoNetworkedField]
    public TimeSpan Duration = TimeSpan.FromSeconds(1);
}
