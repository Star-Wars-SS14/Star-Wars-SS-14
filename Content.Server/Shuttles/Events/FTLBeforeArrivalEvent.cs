namespace Content.Server.Shuttles.Events;

/// <summary>
/// Allows a travel controller to revalidate or adjust the destination immediately before arrival.
/// Cancellation keeps the shuttle in hyperspace and retries arrival one second later.
/// </summary>
[ByRefEvent]
public record struct FTLBeforeArrivalEvent
{
    public bool Cancelled;
}
