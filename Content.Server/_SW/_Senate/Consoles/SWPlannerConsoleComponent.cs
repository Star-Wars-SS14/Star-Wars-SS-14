namespace Content.Server._SW._Senate.Consoles;

[RegisterComponent]
public sealed partial class SWPlannerConsoleComponent : Component
{
    [DataField]
    public string IdSlot = "senate_id_slot";

    public string? ViewPerson;
    public int ViewDay = -1;
}
