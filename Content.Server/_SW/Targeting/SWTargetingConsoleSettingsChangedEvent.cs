namespace Content.Server._SW.Targeting;

public sealed class SWTargetingConsoleSettingsChangedEvent(EntityUid user) : EntityEventArgs
{
    public EntityUid User = user;
}
