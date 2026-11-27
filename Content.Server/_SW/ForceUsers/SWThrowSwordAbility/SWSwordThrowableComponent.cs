namespace Content.Server._SW;

[RegisterComponent]
public sealed partial class SWSwordThrowableComponent : Component
{
    /// <summary>
    /// Lets a thrown saber return to a free hand even when the active hand is occupied.
    /// </summary>
    [DataField]
    public bool ReturnToAnyHand;
}
