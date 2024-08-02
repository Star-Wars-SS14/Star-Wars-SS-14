namespace Content.Shared.Echo.GameTicking.Components;

[RegisterComponent]
public sealed partial class CapturableStationComponent : Component
{
    [DataField]
    public SectorFractions Fraction = SectorFractions.Alliance;
}

public enum SectorFractions
{
    Alliance,
    Resistance,
}
