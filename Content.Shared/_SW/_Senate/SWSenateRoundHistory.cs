using System;
using Robust.Shared.Serialization;

namespace Content.Shared._SW._Senate;

[Serializable, NetSerializable]
public sealed class SWSenateRoundHistory
{
    public int Days;
    public SWSenateHistorySector[] Sectors = Array.Empty<SWSenateHistorySector>();
    public string[] DepartmentSummary = Array.Empty<string>();
    public string[] Projects = Array.Empty<string>();
    public string[] Events = Array.Empty<string>();
    public SWSenateHistoryStat[] Stats = Array.Empty<SWSenateHistoryStat>();
    public SWSenateHistoryLaw[] Laws = Array.Empty<SWSenateHistoryLaw>();
    public int AdditionalLaws;
    public string[] Participants = Array.Empty<string>();
    public int PassedLaws;
    public int PassedAmendments;
}

[Serializable, NetSerializable]
public sealed class SWSenateHistoryStat
{
    public string Name = string.Empty;
    public float Start;
    public float End;
}

[Serializable, NetSerializable]
public sealed class SWSenateHistoryLaw
{
    public string Name = string.Empty;
    public string Author = string.Empty;
    public bool Amendment;
    public bool Repealed;
}

[Serializable, NetSerializable]
public sealed class SWSenateHistorySector
{
    public string Name = string.Empty;
    public SWSenateHistoryStat[] Stats = Array.Empty<SWSenateHistoryStat>();
}
