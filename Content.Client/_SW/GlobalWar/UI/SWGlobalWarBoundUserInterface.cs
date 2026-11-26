namespace Content.Client._SW.GlobalWar.UI;

/// <summary>Keeps Global War UI requests in the same predicted stream as opening and closing the interface.</summary>
public abstract class SWGlobalWarBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private EntityUid? _viewer;
    private bool _disposed;

    protected override void Open()
    {
        base.Open();
        _viewer = PlayerManager.LocalEntity;
    }

    private bool CanSend => !_disposed && IsOpened && _viewer is { } viewer &&
        PlayerManager.LocalEntity == viewer && UiSystem.IsUiOpen(Owner, UiKey, viewer);

    // Ordinary network messages can overtake the predicted interaction that opens the UI.
    public new void SendMessage(BoundUserInterfaceMessage message) => SendPredictedMessage(message);

    public new void SendPredictedMessage(BoundUserInterfaceMessage message)
    {
        if (CanSend)
            base.SendPredictedMessage(message);
    }

    protected override void Dispose(bool disposing)
    {
        // Disposing windows invokes OnClose and may also invoke their control callbacks.
        // The engine has already removed this BUI; do not send another close or button request.
        _disposed = true;
        IsOpened = false;
        _viewer = null;
        base.Dispose(disposing);
    }
}
