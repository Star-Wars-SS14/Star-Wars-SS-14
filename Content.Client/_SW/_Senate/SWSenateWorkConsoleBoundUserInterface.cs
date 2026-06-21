using Content.Shared._SW._Senate;

namespace Content.Client._SW._Senate;

public sealed class SWSenateWorkConsoleBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private SWSenateWorkConsoleMenu? _menu;

    public SWSenateWorkConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _menu = new SWSenateWorkConsoleMenu(this);
        _menu.OnClose += Close;
        _menu.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not SWSenateWorkConsoleState castState)
            return;

        _menu?.SetState(castState);
    }

    public void TakeOffice()
    {
        SendMessage(new SWSenateTakeOfficeMessage());
    }

    public void Resign()
    {
        SendMessage(new SWSenateResignMessage());
    }

    public void SubmitBill(string name, string description, Dictionary<string, float> effects)
    {
        SendMessage(new SWSenateSubmitBillMessage(name, description, effects));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;

        _menu?.Dispose();
    }
}
