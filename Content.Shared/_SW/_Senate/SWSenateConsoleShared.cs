using System.Collections.Generic;
using Robust.Shared.Serialization;

namespace Content.Shared._SW._Senate;

[Serializable, NetSerializable]
public enum SWStatisticsConsoleUiKey
{
    Key
}

[Serializable, NetSerializable]
public enum SWSectorSelectConsoleUiKey
{
    Key
}

[Serializable, NetSerializable]
public sealed class SWStatEntry
{
    public string Id = string.Empty;
    public string Name = string.Empty;
    public string ShortName = string.Empty;
    public Color Color;
    public float Value;
    public float Min;
    public float Max;
}

[Serializable, NetSerializable]
public sealed class SWSectorEntry
{
    public string Id = string.Empty;
    public string Name = string.Empty;
    public Color Color;
    public bool Claimed;
    public List<SWStatEntry> Stats = new();
}

[Serializable, NetSerializable]
public sealed class SWStatisticsConsoleState : BoundUserInterfaceState
{
    public List<SWStatEntry> RepublicStats;
    public List<SWSectorEntry> Sectors;

    public SWStatisticsConsoleState(List<SWStatEntry> republicStats, List<SWSectorEntry> sectors)
    {
        RepublicStats = republicStats;
        Sectors = sectors;
    }
}

[Serializable, NetSerializable]
public sealed class SWSectorSelectConsoleState : BoundUserInterfaceState
{
    public List<SWSectorEntry> Sectors;
    public bool HasCard;
    public bool IsSenatorCard;
    public string? CurrentSector;
    public string? CurrentSectorName;

    public SWSectorSelectConsoleState(List<SWSectorEntry> sectors, bool hasCard, bool isSenatorCard, string? currentSector, string? currentSectorName)
    {
        Sectors = sectors;
        HasCard = hasCard;
        IsSenatorCard = isSenatorCard;
        CurrentSector = currentSector;
        CurrentSectorName = currentSectorName;
    }
}

[Serializable, NetSerializable]
public sealed class SWSelectSectorMessage : BoundUserInterfaceMessage
{
    public string SectorId;

    public SWSelectSectorMessage(string sectorId)
    {
        SectorId = sectorId;
    }
}

[Serializable, NetSerializable]
public enum SWSenateInfoConsoleUiKey
{
    Key
}

[Serializable, NetSerializable]
public sealed class SWSectorOccupancy
{
    public string Id = string.Empty;
    public string Name = string.Empty;
    public Color Color;
    public string? Holder;
}

[Serializable, NetSerializable]
public sealed class SWSenateInfoConsoleState : BoundUserInterfaceState
{
    public List<SWSectorOccupancy> Sectors;
    public string? Chancellor;
    public string? Speaker;

    public SWSenateInfoConsoleState(List<SWSectorOccupancy> sectors, string? chancellor, string? speaker)
    {
        Sectors = sectors;
        Chancellor = chancellor;
        Speaker = speaker;
    }
}

[Serializable, NetSerializable]
public enum SWSenateWorkConsoleUiKey
{
    Key
}

[Serializable, NetSerializable]
public sealed class SWSenateRosterEntry
{
    public string Position = string.Empty;
    public string Holder = string.Empty;
    public Color Color;
}

[Serializable, NetSerializable]
public sealed class SWSenateWorkConsoleState : BoundUserInterfaceState
{
    public bool HasCard;
    public bool IsSenateCard;
    public SWSenatePosition Position;
    public string PositionLabel = string.Empty;
    public bool InOffice;
    public bool CanTake;
    public bool CanResign;
    public string? SectorName;
    public List<SWStatEntry> SectorStats = new();
    public List<int> SectorTrends = new();
    public List<SWStatEntry> RepublicStats = new();
    public List<SWSenateRosterEntry> Roster = new();
    public List<SWBillEntry> Bills = new();
    public int DraftBudget;
}

[Serializable, NetSerializable]
public sealed class SWBillEffectEntry
{
    public string StatName = string.Empty;
    public Color Color;
    public float Delta;
}

[Serializable, NetSerializable]
public sealed class SWBillEntry
{
    public int Id;
    public string Name = string.Empty;
    public string Author = string.Empty;
    public string Stage = string.Empty;
    public List<SWBillEffectEntry> Effects = new();
}

[Serializable, NetSerializable]
public sealed class SWSenateSubmitBillMessage : BoundUserInterfaceMessage
{
    public string Name;
    public string Description;
    public Dictionary<string, float> Effects;

    public SWSenateSubmitBillMessage(string name, string description, Dictionary<string, float> effects)
    {
        Name = name;
        Description = description;
        Effects = effects;
    }
}

[Serializable, NetSerializable]
public sealed class SWSenateTakeOfficeMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class SWSenateResignMessage : BoundUserInterfaceMessage
{
}
