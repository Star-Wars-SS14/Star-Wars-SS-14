using Content.Shared._SW.TaskSystem;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.TaskSystem.Subtask;

[RegisterComponent]
public sealed partial class SWSubtaskContainerComponent : Component
{
    [DataField]
    public List<Subtask> Subtasks = new List<Subtask>();
}

[Serializable, DataDefinition]
public sealed partial class Subtask
{
    [DataField(required: true)]
    public string TaskProto = "";

    public Entity<SWTaskComponent> SpawnedEntity;
}
