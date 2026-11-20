namespace Content.Client._SW.SaberForms;

/// <summary>A client-side trail cast by this swing animation.</summary>
[RegisterComponent]
public sealed partial class SWSaberSwingTrailVisualsComponent : Component
{
    [DataField(required: true)]
    public SWSaberSwingTrail Shape;

    public Angle Direction;
    public Angle Sweep;
    public float Reach;
    public Color Tint = Color.White;
    public TimeSpan StartedAt;
}

public enum SWSaberSwingTrail : byte
{
    Crescent,
    Thrust,
    GuardSweep,
}
