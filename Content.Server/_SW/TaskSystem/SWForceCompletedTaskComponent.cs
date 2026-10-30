namespace Content.Server._SW.TaskSystem;

/// <summary>
/// A task an admin completed by command. Its progress stays at 100% whatever the task's own checks say.
/// </summary>
[RegisterComponent, Access(typeof(SWTasksGameRuleSystem))]
public sealed partial class SWForceCompletedTaskComponent : Component
{
}
