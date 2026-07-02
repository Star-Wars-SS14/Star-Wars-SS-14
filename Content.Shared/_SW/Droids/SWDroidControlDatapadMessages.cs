using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._SW.Droids;

[Serializable, NetSerializable]
public sealed class SWDroidControlDatapadBoundUserInterfaceState : BoundUserInterfaceState
{
    public Dictionary<string, bool> JobsAllowed = new();
    public Dictionary<string, string> AvailableFactions = new();
    public List<SWDroidControlDatapadDroidEntry> Droids = new();
    public List<SWDroidControlDatapadDroidEntry> MapDroids = new();
    public NetEntity? MapUid;
    public bool TargetHumanoidsWithoutId;
    public bool TargetAnimals;

    public SWDroidControlDatapadBoundUserInterfaceState(
        Dictionary<string, bool> jobsAllowed,
        Dictionary<string, string> availableFactions,
        List<SWDroidControlDatapadDroidEntry> droids,
        List<SWDroidControlDatapadDroidEntry> mapDroids,
        NetEntity? mapUid,
        bool targetHumanoidsWithoutId,
        bool targetAnimals)
    {
        JobsAllowed = jobsAllowed;
        AvailableFactions = availableFactions;
        Droids = droids;
        MapDroids = mapDroids;
        MapUid = mapUid;
        TargetHumanoidsWithoutId = targetHumanoidsWithoutId;
        TargetAnimals = targetAnimals;
    }
}

[Serializable, NetSerializable]
public sealed class SWDroidControlDatapadDroidEntry
{
    public NetEntity DroidUid;
    public NetCoordinates Coordinates;
    public string Name;
    public string CoordinatesText;
    public string Charge;
    public string Status;

    public SWDroidControlDatapadDroidEntry(
        NetEntity droidUid,
        NetCoordinates coordinates,
        string name,
        string coordinatesText,
        string charge,
        string status)
    {
        DroidUid = droidUid;
        Coordinates = coordinates;
        Name = name;
        CoordinatesText = coordinatesText;
        Charge = charge;
        Status = status;
    }
}
