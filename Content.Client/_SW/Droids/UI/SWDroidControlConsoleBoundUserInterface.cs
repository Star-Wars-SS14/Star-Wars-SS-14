using Content.Shared._SW.Droids;
using Robust.Client.UserInterface;

namespace Content.Client._SW.Droids.UI;

public sealed class SWDroidControlConsoleBoundUserInterface : BoundUserInterface
{
    private SWDroidControlConsoleMenu? _menu;

    public SWDroidControlConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _menu = this.CreateWindow<SWDroidControlConsoleMenu>();
        _menu.OnDatapadTogglePressed += SendToggleMessage;
        _menu.OpenCentered();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing || _menu == null)
            return;

        _menu.OnDatapadTogglePressed -= SendToggleMessage;
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is SWDroidControlConsoleBoundUserInterfaceState consoleState)
            _menu?.UpdateState(consoleState);
    }

    private void SendToggleMessage(NetEntity datapadUid)
    {
        SendMessage(new SWDroidControlConsoleToggleDatapadMessage(datapadUid));
    }
}
