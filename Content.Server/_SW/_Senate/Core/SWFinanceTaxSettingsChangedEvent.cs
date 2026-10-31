namespace Content.Server._SW._Senate.Core;

/// <summary>A tax input used by accounting changed; null id denotes the base rate.</summary>
public sealed class SWFinanceTaxSettingsChangedEvent(string? id, bool relief) : EntityEventArgs
{
    public string? Id { get; } = id;
    public bool Relief { get; } = relief;
}
