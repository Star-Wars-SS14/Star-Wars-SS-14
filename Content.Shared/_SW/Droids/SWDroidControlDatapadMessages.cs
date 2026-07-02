using Robust.Shared.Serialization;

namespace Content.Shared._SW.Droids;

[Serializable, NetSerializable]
public sealed class SWDroidControlDatapadBoundUserInterfaceState : BoundUserInterfaceState
{
    public Dictionary<string, bool> JobsAllowed = new();
    public Dictionary<string, string> AvailableFactions = new();
    public List<SWDroidControlDatapadDroidEntry> Droids = new();
    public bool TargetHumanoidsWithoutId;
    public bool TargetAnimals;

    public SWDroidControlDatapadBoundUserInterfaceState(
        Dictionary<string, bool> jobsAllowed,
        Dictionary<string, string> availableFactions,
        List<SWDroidControlDatapadDroidEntry> droids,
        bool targetHumanoidsWithoutId,
        bool targetAnimals)
    {
        JobsAllowed = jobsAllowed;
        AvailableFactions = availableFactions;
        Droids = droids;
        TargetHumanoidsWithoutId = targetHumanoidsWithoutId;
        TargetAnimals = targetAnimals;
    }
}

[Serializable, NetSerializable]
public sealed class SWDroidControlDatapadDroidEntry
{
    public string Name;
    public string Coordinates;
    public string Charge;
    public string Status;

    public SWDroidControlDatapadDroidEntry(string name, string coordinates, string charge, string status)
    {
        Name = name;
        Coordinates = coordinates;
        Charge = charge;
        Status = status;
    }
}
