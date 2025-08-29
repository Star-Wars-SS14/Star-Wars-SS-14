using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.TaskSystem.TargetSelector;

[RegisterComponent]
//Actually chooses certain person with certain job
public sealed partial class SWTargetSelectorComponent : Component
{
    [DataField]
    public List<string> Factions = new();

    [DataField]
    public List<string> Jobs = new();

    /// <summary>
    /// Should system select target when task init, or target will be selected by something else?
    /// </summary>
    [DataField("select")]
    public bool SelectTargetOnInit = true;

    public EntityUid? Target = null;

    public string? JobOfTarget = null;
}
