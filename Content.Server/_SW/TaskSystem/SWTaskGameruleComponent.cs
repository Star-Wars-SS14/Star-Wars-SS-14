using Content.Shared._SW.TaskSystem;
using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.TaskSystem;

public sealed partial class SWTaskGameruleComponent : Component
{
    [DataField]
    public List<EntProtoId<SWFactionComponent>> Factions; //задаем в прототипе, какие фракции могут быть

    public List<Entity<SWFactionComponent>> InGameFactions; // коллекция фракций, которые уже были заспавнены в игре в качестве ентити
}
