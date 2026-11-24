namespace Content.Server._SW.Tutorial;

/// <summary>A private practice projectile fired with the exercise weapon wielded at the time of the shot.</summary>
[RegisterComponent]
public sealed partial class SWTutorialPracticeShotComponent : Component
{
    public EntityUid Student;
    public Guid Attempt;
}
