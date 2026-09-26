namespace Content.Server._SW.Targeting;

public sealed class SWTargetingProviderAvailableEvent(EntityUid? user, bool popup) : CancellableEntityEventArgs
{
    public EntityUid? User = user;
    public bool Popup = popup;
}
