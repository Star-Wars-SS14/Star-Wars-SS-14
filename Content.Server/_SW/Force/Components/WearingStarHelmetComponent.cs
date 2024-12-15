namespace Content.Server._SW.Force.Components;

[RegisterComponent]
public sealed partial class WearingStarHelmetComponent : Component
{
    [DataField("helmet")]
    public EntityUid Helmet;
}
