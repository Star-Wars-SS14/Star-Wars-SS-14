using Content.Shared._SW.Economy;
using Robust.Client.UserInterface;

namespace Content.Client._SW.Economy;

public sealed class SWSpireMonitorBoundUserInterface : BoundUserInterface
{
    private SWSpireMonitorWindow? _window;

    public SWSpireMonitorBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = new SWSpireMonitorWindow(this);
        _window.OpenCentered();
        _window.OnClose += Close;
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is SWSpireMonitorBoundUserInterfaceState monitor)
            _window?.UpdateState(monitor);
    }

    public void SendSelect()
    {
        SendMessage(new SWSpireMonitorSelectMessage());
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _window?.Dispose();
    }
}