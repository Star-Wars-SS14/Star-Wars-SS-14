namespace Content.Server._SW.Droids;

[RegisterComponent]
public sealed partial class SWDroidControlConsoleComponent : Component
{
    [DataField]
    public string Faction = string.Empty;
}
