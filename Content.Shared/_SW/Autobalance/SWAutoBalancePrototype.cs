using Content.Server._SW.TaskSystem;
using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.Autobalance;

[Prototype("swAutoBalance")]
public sealed partial class SWAutoBalancePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField]
    public ProtoId<SWFactionRelatedJobsPrototype> FirstFactionRelatedJobsId = "SWRepublicRelatedJobs";

    [DataField]
    public ProtoId<SWFactionRelatedJobsPrototype> SecondRelatedJobsId = "SWEmpireRelatedJobs";

    /// <summary>
    /// The number of players in the second faction equal to 1 player in the first faction
    /// </summary>
    [DataField]
    public int Ratio = 2;
}
