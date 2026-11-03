using Robust.Shared.Serialization;

namespace Content.Shared._SW.GlobalWar;

/// <summary>Political ownership; Neutral is a real owner, not missing information.</summary>
[Serializable, NetSerializable]
public enum SWGlobalWarFaction : byte
{
    Neutral,
    Republic,
    Empire,
}

/// <summary>Strategic role of a sector, independent of its current owner.</summary>
[Serializable, NetSerializable]
public enum SWGlobalWarSectorKind : byte
{
    Capital,
    Agriculture,
    Mining,
    Industry,
    Force,
    Tourism,
}

/// <summary>A visited location can be paused but never recreated from its initial map.</summary>
public enum SWGlobalWarLocationState : byte
{
    Unloaded,
    Loading,
    Active,
    Paused,
    Failed,
    Destroyed,
}
