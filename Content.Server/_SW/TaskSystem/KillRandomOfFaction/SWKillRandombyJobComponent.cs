using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.TaskSystem.KillRandomOfFaction;
[RegisterComponent]
public sealed partial class SWKillRandomByJobComponent : Component
{
    [DataField]
    public List<string> Factions = new();

    [DataField]
    public Dictionary<string, int> JobsToScores = new();

    [DataField]
    public Dictionary<string, LocId> JobsToDescriptions = new();

    public string? JobProtoToKill = null;

    public string? JobProtoOfKilled = null;

    ///<summary>
    ///Если true, то для выполнения задания достаточно убить игрока, у которой есть любая из должностей JobToScores.
    ///Иначе выбирается одна конкретная должность из списка JobToScores, и нужно убить игрока именно с этой должностью
    /// </summary>
    [DataField]
    public bool KillAny = false;
}
