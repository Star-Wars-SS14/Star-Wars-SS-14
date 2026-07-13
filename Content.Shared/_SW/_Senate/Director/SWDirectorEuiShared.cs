using Content.Shared.Eui;
using Robust.Shared.Serialization;

namespace Content.Shared._SW._Senate.Director;

[Serializable, NetSerializable]
public enum SWDirectorMode : byte
{
    Off,
    Autopilot,
    Manual,
}

[Serializable, NetSerializable]
public enum SWDirectorActionKind : byte
{
    Maneuvers,
    Provocation,
    Sanction,
    LiftSanction,
    Blockade,
}

[Serializable, NetSerializable]
public sealed class SWDirectorSectorEntry
{
    public string Id = string.Empty;
    public string Name = string.Empty;
    public string Holder = string.Empty;
    public bool Sanctioned;
    public string SanctionLabel = string.Empty;
}

[Serializable, NetSerializable]
public sealed class SWDirectorEuiState : EuiStateBase
{
    public SWDirectorMode Mode;
    public string ControllerName = string.Empty;
    public bool IAmController;
    public int Influence;
    public int InfluenceCap;
    public string IncomeLine = string.Empty;
    public int BorderPressure;
    public string BorderStatus = string.Empty;
    public bool BlockadeActive;
    public string BlockadeLabel = string.Empty;
    public string Notice = string.Empty;
    public List<SWDirectorSectorEntry> Sectors = new();
    public List<string> Chronicle = new();
    public int CostManeuvers;
    public int CostProvocation;
    public int CostSanction;
    public int CostBlockade;
}

[Serializable, NetSerializable]
public sealed class SWDirectorSetModeMessage : EuiMessageBase
{
    public SWDirectorMode Mode;

    public SWDirectorSetModeMessage(SWDirectorMode mode)
    {
        Mode = mode;
    }
}

[Serializable, NetSerializable]
public sealed class SWDirectorActionMessage : EuiMessageBase
{
    public SWDirectorActionKind Kind;
    public string SectorId = string.Empty;

    public SWDirectorActionMessage(SWDirectorActionKind kind, string sectorId = "")
    {
        Kind = kind;
        SectorId = sectorId;
    }
}
