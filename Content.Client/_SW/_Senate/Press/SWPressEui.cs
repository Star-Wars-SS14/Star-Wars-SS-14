using Content.Client.Eui;
using Content.Shared._SW._Senate.Press;
using Content.Shared.Eui;
using JetBrains.Annotations;

namespace Content.Client._SW._Senate.Press;

[UsedImplicitly]
public sealed class SWPressEui : BaseEui
{
    private readonly SWPressWindow _window = new();

    public SWPressEui()
    {
        _window.OnClose += () => SendMessage(new CloseEuiMessage());
        _window.Action += SendMessage;
    }

    public override void Opened() { base.Opened(); _window.OpenCentered(); }
    public override void Closed() { base.Closed(); _window.Close(); }
    public override void HandleState(EuiStateBase state)
    {
        if (state is SWPressEuiState press)
            _window.SetState(press);
    }
}
