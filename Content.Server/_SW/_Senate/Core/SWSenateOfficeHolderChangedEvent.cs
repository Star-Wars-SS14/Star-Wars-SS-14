namespace Content.Server._SW._Senate.Core;

/// <summary>Only actual office transitions; actor is null when identity is not provable.</summary>
public sealed class SWSenateOfficeHolderChangedEvent(string owner, EntityUid? actor) : EntityEventArgs
{
    public readonly string Owner = owner;
    public readonly EntityUid? Actor = actor;
}
