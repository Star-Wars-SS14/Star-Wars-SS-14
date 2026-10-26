using Content.Client.Eui;
using Content.Shared._SW._Senate.Director;
using Content.Shared.Eui;
using JetBrains.Annotations;

namespace Content.Client._SW._Senate.Director;

[UsedImplicitly]
public sealed class SWDirectorEui : BaseEui
{
    private readonly SWDirectorWindow _window;

    public SWDirectorEui()
    {
        _window = new SWDirectorWindow();
        _window.OnClose += () => SendMessage(new CloseEuiMessage());
        _window.OnSetMode += mode => SendMessage(new SWDirectorSetModeMessage(mode));
        _window.OnAction += (kind, sectorId, param, target) => SendMessage(new SWDirectorActionMessage(kind, sectorId, param, target));
        _window.OnSendNote += (text, isPublic) => SendMessage(new SWDirectorSendNoteMessage(text, isPublic));
        _window.OnDefenseEvent += message => SendMessage(message);
    }

    public override void Opened()
    {
        base.Opened();
        _window.OpenCentered();
    }

    public override void Closed()
    {
        base.Closed();
        _window.Close();
    }

    public override void HandleState(EuiStateBase state)
    {
        if (state is not SWDirectorEuiState castState)
            return;

        _window.SetState(castState);
    }
}
