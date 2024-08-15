using Content.Client.Xenoarchaeology.Ui;
using Content.Shared.Echo.CaptureConsole;
using Content.Shared.Xenoarchaeology.Equipment;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client.Echo.Capture.Ui;

[UsedImplicitly]
public sealed class CaptureConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    [ViewVariables]
    private CaptureConsoleMenu? _consoleMenu;


    protected override void Open()
    {
        base.Open();

        _consoleMenu = this.CreateWindow<CaptureConsoleMenu>();

        _consoleMenu.OnCaptureButtonPressed += OnButtonPressed;
    }

    private void OnButtonPressed()
    {
        SendMessage(new CaptureConsoleButtonPressedMessage());
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        switch (state)
        {
            case CaptureConsoleUpdateState msg:
                _consoleMenu?.UpdateInformationDisplay(msg);
                //  _consoleMenu?.UpdateProgressBar(msg);
                break;
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
            return;

        _consoleMenu?.Dispose();
    }
}
