using Robust.Shared.Prototypes;

namespace Content.Server._SW.TaskSystem.ParrallelTask;

/// <summary>
/// При выдаче задания с этим компонентом одновременно фракции Faction будет выдно еще одно задание, указанное в Task.
/// </summary>
[RegisterComponent]
public sealed partial class SWParallelTaskComponent : Component
{
    [DataField]
    public EntProtoId Task = default;

    [DataField]
    public string Faction = default;
}
