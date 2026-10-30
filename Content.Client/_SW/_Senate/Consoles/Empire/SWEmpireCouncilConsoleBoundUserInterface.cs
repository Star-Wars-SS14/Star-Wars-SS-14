using Content.Shared._SW._Senate.Empire;

namespace Content.Client._SW._Senate.Consoles.Empire;

public sealed class SWEmpireCouncilConsoleBoundUserInterface : BoundUserInterface
{
    private SWEmpireCouncilConsoleMenu? _menu;

    public SWEmpireCouncilConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey) { }

    protected override void Open()
    {
        base.Open();
        _menu = new SWEmpireCouncilConsoleMenu(this);
        _menu.OnClose += Close;
        _menu.OpenCentered();
    }

    public void QuoteIntelligence(SWEmpireIntelligenceRequest request) => SendMessage(new SWEmpireIntelligenceQuoteMessage(request));
    public void SubmitIntelligence(SWEmpireIntelligenceRequest request) => SendMessage(new SWEmpireIntelligenceCommandMessage(request));
    public void QuoteDiplomacy(SWEmpireDiplomacyRequest request) => SendMessage(new SWEmpireDiplomacyQuoteMessage(request));
    public void SubmitDiplomacy(SWEmpireDiplomacyRequest request) => SendMessage(new SWEmpireDiplomacyCommandMessage(request));
    public void BreakDiplomacy(int id) => SendMessage(new SWEmpireDiplomacyBreakMessage(id));
    public void QuoteMilitary(SWEmpireMilitaryRequest request) => SendMessage(new SWEmpireMilitaryQuoteMessage(request));
    public void SubmitMilitary(SWEmpireMilitaryRequest request) => SendMessage(new SWEmpireMilitaryCommandMessage(request));
    public void Quote(SWEmpireIndustryRequest request) => SendMessage(new SWEmpireIndustryQuoteMessage(request));
    public void Submit(SWEmpireIndustryRequest request) => SendMessage(new SWEmpireIndustryCommandMessage(request));
    public void QuoteMaterial(SWEmpireMaterialRequest request) => SendMessage(new SWEmpireMaterialQuoteMessage(request));
    public void SubmitMaterial(SWEmpireMaterialRequest request) => SendMessage(new SWEmpireMaterialCommandMessage(request));
    public void RejectMaterial(string requestId) => SendMessage(new SWEmpireMaterialRejectMessage(requestId));
    public void Transfer(SWEmpireCouncilSphere target, int amount, string requestId) =>
        SendMessage(new SWEmpireBudgetTransferMessage(target, amount, requestId));

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);
        if (message is SWEmpireCouncilUpdateMessage update) _menu?.SetState(update.State);
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is SWEmpireCouncilConsoleState council) _menu?.SetState(council);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _menu?.Dispose();
    }
}
