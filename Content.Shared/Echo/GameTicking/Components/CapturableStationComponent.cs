namespace Content.Shared.Echo.GameTicking.Components;

[RegisterComponent]
public sealed partial class CapturableStationComponent : Component
{
    [DataField]
    public string Fraction = "EmpireFaction";
}

public enum SectorFractions
{
    Alliance,
    Resistance,
}
