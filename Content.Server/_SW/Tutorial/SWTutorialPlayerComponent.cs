namespace Content.Server._SW.Tutorial;

/// <summary>
/// On a student's body. Keeps the student out of the ghost warp list and the round end summary of the real round.
/// </summary>
[RegisterComponent]
[Access(typeof(SWTutorialSystem))]
public sealed partial class SWTutorialPlayerComponent : Component;
