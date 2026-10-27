namespace Content.Server.Shuttles.Events;

//SW start
// GlobalWar needs to recheck the arrival area because another ship may occupy it during the flight.
/// <summary>
/// Allows a travel controller to revalidate or adjust the destination immediately before arrival.
/// Cancellation keeps the shuttle in hyperspace and retries arrival one second later.
/// </summary>
[ByRefEvent]
public record struct FTLBeforeArrivalEvent
{
    public bool Cancelled;
}
//SW end
