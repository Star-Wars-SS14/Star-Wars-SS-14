namespace Content.Server._SW._Senate.Core;

/// <summary>A new paid aid order was accepted.</summary>
public sealed class SWHealthAidOrderedEvent(string orderId, string worldId, int episode) : EntityEventArgs
{
    public string OrderId { get; } = orderId;
    public string WorldId { get; } = worldId;
    public int Episode { get; } = episode;
}

/// <summary>Delivered aid increased the target world's reserve.</summary>
public sealed class SWHealthAidAppliedEvent(string orderId, string worldId, int episode, float reserveIncrease) : EntityEventArgs
{
    public string OrderId { get; } = orderId;
    public string WorldId { get; } = worldId;
    public int Episode { get; } = episode;
    public float ReserveIncrease { get; } = reserveIncrease;
}