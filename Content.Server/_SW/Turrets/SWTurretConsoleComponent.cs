using Content.Server.Administration.Commands;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.TurretConsole;
[RegisterComponent]
public sealed partial class SWTurretConsoleComponent : Component
{
    [DataField]
    public Dictionary<string, bool> JobsAllowed = new Dictionary<string, bool>();

    //Jobs that can be allowed or disallowed. If job isn't in this list, then it won't be displayed
    [DataField]
    public List<string> Jobs = new List<string>() {
        "SWGlobalWarRepublicHead",
        "SWGlobalWarRepublicWarMinister",
        "SWGlobalWarRepublicLogisticsMinister",
        "SWGlobalWarRepublicIndustryMinister",
        "SWGlobalWarRepublicLogisticsWorker",
        "SWGlobalWarRepublicIndustryWorker",
        "SWGlobalWarRepublicSeniorOfficer",
        "SWGlobalWarRepublicJuniorOfficer",
        "SWGlobalWarRepublicPilot",
        "SWGlobalWarRepublicSoldier",
        "SWGlobalWarRepublicForceUser",
        "SWGlobalWarRepublicIntelligence",
        "SWGlobalWarRepublicCitizen",
        "SWGlobalWarEmpireHead",
        "SWGlobalWarEmpireWarMinister",
        "SWGlobalWarEmpireLogisticsMinister",
        "SWGlobalWarEmpireIndustryMinister",
        "SWGlobalWarEmpireLogisticsWorker",
        "SWGlobalWarEmpireIndustryWorker",
        "SWGlobalWarEmpireSeniorOfficer",
        "SWGlobalWarEmpireJuniorOfficer",
        "SWGlobalWarEmpirePilot",
        "SWGlobalWarEmpireSoldier",
        "SWGlobalWarEmpireForceUser",
        "SWGlobalWarEmpireIntelligence",
        "SWGlobalWarEmpireCitizen",
        "SWBlackSun",
        "SWBlackSunMercenary",
        "SWCivialian",
        "SWEmpireIntelligenceAgent",
        "SWEmpireIntelligenceCaretaker",
        "SWEmpireLieutenant",
        "SWEmpireMajor",
        "SWEmpireMedic",
        "SWEmpireMoff",
        "SWEmpireSoldier",
        "SWExchangeCaptain",
        "SWExchangeOperative",
        "SWJedi",
        "SWMandalorianLeader",
        "SWMandalorianRecruit",
        "SWMandalorianVeteran",
        "SWMandalorianWarrior",
        "SWNeutralBartender",
        "SWRepublicCaptain",
        "SWRepublicCommanderCommander",
        "SWRepublicCommanderLieutenant",
        "SWRepublicCommanderMedic",
        "SWRepublicCommanderSniper",
        "SWRepublicCommanderStormtrooper",
        "SWRepublicGeneral",
        "SWRepublicLieutenant",
        "SWRepublicMedic",
        "SWRepublicRecruit",
        "SWRepublicSoldier",
        "SWSith",
        "SWSobrikCaptain",
        "SWSobrikMedic",
        "SWSobrikOfficer"
    };
}
