using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Ministries;

namespace Content.Client._SW._Senate.Consoles.MinistryWork;

public sealed class SWMinistryWorkConsoleBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private SWMinistryWorkConsoleMenu? _menu;

    public SWMinistryWorkConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _menu = new SWMinistryWorkConsoleMenu(this);
        _menu.OnClose += Close;
        _menu.OpenCentered();
    }

    public void AuditRequest(int requestId)
    {
        SendMessage(new SWMinistryAuditRequestMessage(requestId));
    }

    public void ResolveRequest(int requestId, int optionIndex)
    {
        SendMessage(new SWMinistryResolveRequestMessage(requestId, optionIndex));
    }

    public void RefuseRequest(int requestId, string reason)
    {
        SendMessage(new SWMinistryRefuseRequestMessage(requestId, reason));
    }

    public void Resign()
    {
        SendMessage(new SWMinistryResignMessage());
    }

    public void SetStance(SWDefenseStance stance)
    {
        SendMessage(new SWDefenseSetStanceMessage(stance));
    }

    public void SelectProgram(string programId)
    {
        SendMessage(new SWDefenseSelectProgramMessage(programId));
    }

    public void DeployFleet(int groupId, string target)
    {
        SendMessage(new SWFleetDeployMessage(groupId, target));
    }

    public void LaunchOp(string opId, string target)
    {
        SendMessage(new SWDefenseLaunchOpMessage(opId, target));
    }

    public void SetPosture(SWSecurityPosture posture)
    {
        SendMessage(new SWSecuritySetPostureMessage(posture));
    }

    public void SelectSecProgram(string programId)
    {
        SendMessage(new SWSecuritySelectProgramMessage(programId));
    }

    public void DeployCell(int cellId, string target)
    {
        SendMessage(new SWSecurityDeployCellMessage(cellId, target));
    }

    public void LaunchSecOp(string opId, string target)
    {
        SendMessage(new SWSecurityLaunchOpMessage(opId, target));
    }

    public void RaidPlot(int plotId)
    {
        SendMessage(new SWSecurityRaidPlotMessage(plotId));
    }

    public void SetFunding(int level)
    {
        SendMessage(new SWHealthSetFundingMessage(level));
    }

    public void DeployVaccine()
    {
        SendMessage(new SWHealthDeployVaccineMessage());
    }

    public void Quarantine(string sectorId, bool on)
    {
        SendMessage(new SWHealthQuarantineMessage(sectorId, on));
    }

    public void BuildHospital(string sectorId)
    {
        SendMessage(new SWHealthBuildHospitalMessage(sectorId));
    }

    public void EmergencyAid(string sectorId)
    {
        SendMessage(new SWHealthEmergencyAidMessage(sectorId));
    }

    public void DeclareHealthEmergency()
    {
        SendMessage(new SWHealthDeclareEmergencyMessage());
    }

    public void SelectHealthProgram(string programId)
    {
        SendMessage(new SWHealthSelectProgramMessage(programId));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not SWMinistryWorkConsoleState castState)
            return;

        _menu?.SetState(castState);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;

        _menu?.Dispose();
    }
}
