namespace Content.Server.Shuttles.Events;

//SW start
// GlobalWar needs to recheck the arrival area because another ship may occupy it during the flight.
/// <summary>
/// Allows a travel controller to revalidate or adjust the destination immediately before arrival.
/// Cancellation preserves the shuttle in hyperspace and permits a rate-limited retry.
/// </summary>
[ByRefEvent]
public record struct FTLBeforeArrivalEvent
{
    public bool Cancelled;
    public TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

    public FTLBeforeArrivalEvent()
    {
    }
}
//SW end
