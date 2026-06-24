namespace Content.Server._SW._Senate.Consoles;

[RegisterComponent]
public sealed partial class SWSenateWorkConsoleComponent : Component
{
    [DataField]
    public string IdSlot = "senate_id_slot";

    [DataField]
    public int DraftBudget;

    [DataField]
    public float MaxEffect = 10f;
}
