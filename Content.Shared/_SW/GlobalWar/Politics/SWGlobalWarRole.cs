using Robust.Shared.Serialization;

namespace Content.Shared._SW.GlobalWar.Politics;

[Serializable, NetSerializable]
public enum SWGlobalWarRole : byte
{
    Head,
    WarMinister,
    LogisticsMinister,
    IndustryMinister,
    LogisticsWorker,
    IndustryWorker,
    SeniorOfficer,
    JuniorOfficer,
    Pilot,
    Soldier,
    ForceUser,
    Intelligence,
    Civilian,
}

[Flags, Serializable, NetSerializable]
public enum SWGlobalWarAuthority : byte
{
    None = 0,
    Diplomacy = 1,
    Treasury = 2,
    WarBudget = 4,
    LogisticsBudget = 8,
    IndustryBudget = 16,
    MilitaryCommand = 32,
    Intelligence = 64,
}
