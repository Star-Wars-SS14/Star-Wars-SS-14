using Content.Server.NPC.Queries.Considerations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.NPC;
public sealed partial class SWHasIdCardCon : UtilityConsideration
{
    [DataField]
    public List<string> AllowedJobs = new List<string>()
    {
        "SWSobrikCaptain",
        "SWSobrikOfficer",
        "SWSobrikMedic",
        "SWEmpireMoff",
        "SWEmpireMajor",
        "SWEmpireLieutenant",
        "SWEmpireSoldier",
        "SWEmpireMedic",
        "SWEmpireIntelligenceCaretaker",
        "SWEmpireIntelligenceAgent"
    };
}
