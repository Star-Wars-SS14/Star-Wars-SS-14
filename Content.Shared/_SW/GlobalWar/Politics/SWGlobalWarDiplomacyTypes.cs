using Robust.Shared.Serialization;

namespace Content.Shared._SW.GlobalWar.Politics;

[Serializable, NetSerializable]
public enum SWGlobalWarRelations : byte
{
    Peace,
    War,
    Truce,
}

[Serializable, NetSerializable]
public enum SWGlobalWarNeutralPhase : byte
{
    CivilWar,
    Annexing,
    Integrated,
}

[Serializable, NetSerializable]
public enum SWGlobalWarPoliticalAction : byte
{
    DeclareWar,
    OfferTruce,
    WithdrawTruce,
    BreakTruce,
    SetSanction,
    OpenBorderConflict,
    Sponsor,
    PublicAddress,
    StateMessage,
    PrepareSurrender,
    ConfirmSurrender,
}

[Serializable, NetSerializable]
public enum SWGlobalWarOutcome : byte
{
    None,
    RepublicVictory,
    EmpireVictory,
    MutualSurrender,
    TimeLimit,
    InsufficientParticipants,
    AdministrativeStop,
}
