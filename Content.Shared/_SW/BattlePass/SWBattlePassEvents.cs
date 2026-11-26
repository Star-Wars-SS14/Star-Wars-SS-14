using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._SW.BattlePass;

/// <summary>
/// What a client may know about its own player. Rules and rewards are in the prototypes; the client gets only the
/// numbers, and everything in here was computed on the server.
/// </summary>
[Serializable, NetSerializable]
public sealed class SWBattlePassState
{
    public string? Season;

    /// <summary>
    /// False before the season starts.
    /// </summary>
    public bool Started;

    /// <summary>
    /// The last day of the season has passed.
    /// </summary>
    public bool Ended;

    public bool XpOpen;
    public bool ClaimsOpen;
    public int Xp;
    public bool Paid;
    public HashSet<string> Claimed = new();
    public HashSet<string> Titles = new();
    public Dictionary<string, int> Cases = new();
    public int Snowflakes;
    public int CasesSincePity;

    /// <summary>
    /// The player has opened a case before, so the next one is an ordinary roll.
    /// </summary>
    public bool FirstCaseOpened;

    public int OvertimeClaimed;
    public int CatchUpBank;
    public bool Evening;

    /// <summary>
    /// This state follows the first-time gift, so the window should say what the gift was.
    /// </summary>
    public bool FirstOpenGift;

    public bool ButtonUnlocked;
    public bool InCombat;
    public List<SWBattlePassQuestState> Quests = new();
    public SWBattlePassRoundSummary? LastRound;
}

[Serializable, NetSerializable]
public sealed class SWBattlePassQuestState
{
    public string Id = string.Empty;
    public bool Weekly;
    public int Progress;
    public bool Done;
}

/// <summary>
/// Where the XP of one round came from, shown to the player.
/// </summary>
[Serializable, NetSerializable]
public sealed class SWBattlePassRoundSummary
{
    public int Minutes;
    public int MinutesXp;
    public int TasksXp;
    public int KillsXp;
    public int EndXp;
    public int WinXp;
    public int FirstOfDayXp;
    public int EveningXp;
    public int CatchUpXp;
    public int QuestsXp;
    public int Total;
}

/// <summary>
/// Client to server: send my state.
/// </summary>
[Serializable, NetSerializable]
public sealed class SWBattlePassRequestStateEvent(bool opening) : EntityEventArgs
{
    /// <summary>
    /// The window is being opened, which is when the first-time gift is given.
    /// </summary>
    public readonly bool Opening = opening;
}

[Serializable, NetSerializable]
public sealed class SWBattlePassStateEvent(SWBattlePassState state) : EntityEventArgs
{
    public readonly SWBattlePassState State = state;
}

/// <summary>
/// Client to server: claim one reward. The server checks everything again; this is a wish, not a fact.
/// </summary>
[Serializable, NetSerializable]
public sealed class SWBattlePassClaimEvent(int tier, SWBattlePassTrack track) : EntityEventArgs
{
    public readonly int Tier = tier;
    public readonly SWBattlePassTrack Track = track;
}

/// <summary>
/// Client to server: claim everything available.
/// </summary>
[Serializable, NetSerializable]
public sealed class SWBattlePassClaimAllEvent : EntityEventArgs;

/// <summary>
/// Client to server: claim the overtime cases earned past the last tier.
/// </summary>
[Serializable, NetSerializable]
public sealed class SWBattlePassClaimOvertimeEvent : EntityEventArgs;

/// <summary>
/// Client to server: open one case of this kind.
/// </summary>
[Serializable, NetSerializable]
public sealed class SWBattlePassOpenCaseEvent(ProtoId<SWBattlePassCasePrototype> caseId) : EntityEventArgs
{
    public readonly ProtoId<SWBattlePassCasePrototype> Case = caseId;
}

/// <summary>
/// Client to server: buy this line of a case with snowflakes.
/// </summary>
[Serializable, NetSerializable]
public sealed class SWBattlePassBuyEntryEvent(ProtoId<SWBattlePassCasePrototype> caseId, int entry) : EntityEventArgs
{
    public readonly ProtoId<SWBattlePassCasePrototype> Case = caseId;
    public readonly int Entry = entry;
}

/// <summary>
/// Server to client: what the case gave. The roll is done before this is sent, the animation only shows it.
/// </summary>
[Serializable, NetSerializable]
public sealed class SWBattlePassCaseResultEvent(
    ProtoId<SWBattlePassCasePrototype> caseId,
    int entry,
    bool duplicate,
    int snowflakes) : EntityEventArgs
{
    public readonly ProtoId<SWBattlePassCasePrototype> Case = caseId;
    public readonly int Entry = entry;
    public readonly bool Duplicate = duplicate;
    public readonly int Snowflakes = snowflakes;
}

/// <summary>
/// Server to client: why a request was refused, as a localized line.
/// </summary>
[Serializable, NetSerializable]
public sealed class SWBattlePassRefusedEvent(string reason) : EntityEventArgs
{
    public readonly string Reason = reason;
}
