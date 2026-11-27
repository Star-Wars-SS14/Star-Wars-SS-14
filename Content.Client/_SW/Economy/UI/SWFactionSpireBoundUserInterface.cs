using Content.Shared._SW.Economy;
using Robust.Client.UserInterface;

namespace Content.Client._SW.Economy;

public sealed class SWFactionSpireBoundUserInterface : BoundUserInterface
{
    private SWFactionSpireWindow? _window;

    public SWFactionSpireBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = new SWFactionSpireWindow(this);
        _window.OpenCentered();
        _window.OnClose += Close;
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is SWFactionSpireBoundUserInterfaceState spire)
            _window?.UpdateState(spire);
    }

    public void SendCapture()
    {
        SendMessage(new SWFactionSpireCaptureMessage());
    }

    public void SendEject()
    {
        SendMessage(new SWFactionSpireEjectMessage());
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _window?.Dispose();
    }
}