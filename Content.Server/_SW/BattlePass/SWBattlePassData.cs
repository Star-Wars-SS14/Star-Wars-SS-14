using System.Text.Json;
using Content.Shared._SW.BattlePass;
using Robust.Shared.Network;

namespace Content.Server._SW.BattlePass;

/// <summary>
/// On a body a player spawned into, so the pass can listen to what happens to it. Changes nothing about the body.
/// </summary>
[RegisterComponent]
public sealed partial class SWBattlePassPlayerComponent : Component;

/// <summary>
/// The part of a player's season that is saved as JSON. See <c>SWBattlePassProgress.State</c>.
/// </summary>
public sealed class SWBattlePassSavedState
{
    private static readonly JsonSerializerOptions Options = new() { IncludeFields = true };

    public HashSet<string> Claimed { get; set; } = new();
    public HashSet<string> Titles { get; set; } = new();
    public Dictionary<string, int> Cases { get; set; } = new();
    public int Snowflakes { get; set; }
    public int CasesSincePity { get; set; }
    public bool FirstCaseOpened { get; set; }
    public int OvertimeClaimed { get; set; }

    public bool FirstOpened { get; set; }
    public bool TutorialPaid { get; set; }
    public int RoundsPlayed { get; set; }

    public int CatchUpBank { get; set; }

    /// <summary>
    /// The last day already counted into the bank.
    /// </summary>
    public string? CatchUpThrough { get; set; }

    public string? LastPlayedDay { get; set; }

    public string? MinutesDay { get; set; }
    public int MinutesToday { get; set; }
    public string? FirstRoundDay { get; set; }

    public string? DailyDay { get; set; }
    public List<string> DailyActive { get; set; } = new();
    public HashSet<string> DailyDone { get; set; } = new();
    public string? WeeklyKey { get; set; }
    public HashSet<string> WeeklyDone { get; set; } = new();

    /// <summary>
    /// Progress of the current quests, by quest ID.
    /// </summary>
    public Dictionary<string, int> QuestProgress { get; set; } = new();

    /// <summary>
    /// All XP by where it came from: the design's first question is what share of it is plain minutes.
    /// </summary>
    public Dictionary<string, int> XpBySource { get; set; } = new();

    public SWBattlePassRoundSummary? LastRound { get; set; }

    public string Serialize()
    {
        return JsonSerializer.Serialize(this, Options);
    }

    public static SWBattlePassSavedState Deserialize(string json)
    {
        return JsonSerializer.Deserialize<SWBattlePassSavedState>(json, Options) ?? new SWBattlePassSavedState();
    }
}

/// <summary>
/// A player's season in memory. Lives while the player is online and until their last change is saved.
/// </summary>
public sealed class SWBattlePassData
{
    public readonly NetUserId User;
    public readonly string Season;
    public int Xp;
    public bool Paid;
    public SWBattlePassSavedState State = new();

    public bool Connected;

    /// <summary>
    /// False for guests whose ID changes on each connection: a row for them would never be read again.
    /// </summary>
    public bool Persist = true;

    public bool Dirty;
    public bool Saving;

    /// <summary>
    /// Cosmetics given in this sitting. Ownership reaches the play-time cache only after its database write, and a
    /// second case opened before that must already count as a duplicate.
    /// </summary>
    public readonly HashSet<string> Granted = new();

    /// <summary>
    /// Real time of the next request this player may make. Stops a modified client from spamming the database.
    /// </summary>
    public TimeSpan NextRequest;

    /// <summary>
    /// True for the state sent right after the first-time gift, so the window can say what the gift was.
    /// </summary>
    public bool GiftJustGiven;

    /// <summary>
    /// The client has been told it can show the button.
    /// </summary>
    public bool UnlockSent;

    public SWBattlePassData(NetUserId user, string season)
    {
        User = user;
        Season = season;
    }
}

/// <summary>
/// What the server saw a player do in the current round. Never saved: XP is paid from it at the end of the round.
/// </summary>
public sealed class SWBattlePassRoundTracker
{
    public int ActiveSeconds;

    /// <summary>
    /// XP for the active time so far: the rate and the seed bonus differ from sample to sample.
    /// </summary>
    public float MinutesXp;

    public int TasksXp;
    public int KillsXp;
    public int QuestsXp;

    public Dictionary<string, int> SecondsByFaction = new();
    public Dictionary<NetUserId, int> KillsByVictim = new();

    public EntityUid? LastMob;
    public Robust.Shared.Map.MapCoordinates? LastPosition;

    /// <summary>
    /// How far the player has walked since the last sample, along the path and not in a straight line.
    /// </summary>
    public float MovedTiles;

    /// <summary>
    /// When the player last moved or did something.
    /// </summary>
    public TimeSpan LastActed;

    public int LifeSeconds;
    public bool OneLifeCounted;
    public bool DiedThisLife;
}
