namespace Content.Shared._SW.TaskSystem;

[RegisterComponent]
public sealed partial class TaskConsoleComponent : Component
{
    [DataField(required: true)]
    public string Faction;
}
