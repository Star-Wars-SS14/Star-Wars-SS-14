using System.Globalization;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared._SW.BattlePass;

[Serializable, NetSerializable]
public enum SWBattlePassTrack : byte
{
    Free,
    Paid,
}

[Serializable, NetSerializable]
public enum SWCosmeticRarity : byte
{
    Common,
    Rare,
    Legendary,
}

/// <summary>
/// One season: dates, price of a tier, how XP is earned, rewards. Everything a client shows comes from here too,
/// so the odds and rewards in the window are the ones the server rolls.
/// </summary>
[Prototype("swBattlePassSeason")]
public sealed partial class SWBattlePassSeasonPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = string.Empty;

    [DataField(required: true)]
    public LocId Name;

    /// <summary>
    /// The picture at the top of the window. Each season has its own.
    /// </summary>
    [DataField]
    public SpriteSpecifier? Banner;

    /// <summary>
    /// First day of the season, <c>yyyy-MM-dd</c>, Moscow time.
    /// </summary>
    [DataField(required: true)]
    public string Start = string.Empty;

    /// <summary>
    /// Last day of the season, <c>yyyy-MM-dd</c>, Moscow time. The day counts in full.
    /// </summary>
    [DataField(required: true)]
    public string End = string.Empty;

    [DataField]
    public int XpPerTier = 2000;

    /// <summary>
    /// The gift of the first opening, so that nobody starts from an empty track.
    /// </summary>
    [DataField]
    public int FirstOpenXp = 2000;

    /// <summary>
    /// What a day of the season without play adds to the catch-up bank.
    /// </summary>
    [DataField]
    public int CatchUpPerDay = 750;

    /// <summary>
    /// Paid instead of a reward the player already owns, for example from a case.
    /// </summary>
    [DataField]
    public int DuplicateRewardSnowflakes = 40;

    [DataField]
    public SWBattlePassXpRules Xp = new();

    /// <summary>
    /// Jobs that earn the civilian rate. Other jobs must have a faction to earn anything.
    /// </summary>
    [DataField]
    public List<ProtoId<JobPrototype>> CivilianJobs = new();

    /// <summary>
    /// The tiers, the first one being tier 1.
    /// </summary>
    [DataField(required: true)]
    public List<SWBattlePassTier> Tiers = new();

    /// <summary>
    /// What each tier's worth of XP past the last tier gives. It comes from play, so it never depends on the paid track.
    /// </summary>
    [DataField]
    public ProtoId<SWBattlePassCasePrototype>? OvertimeCase;

    [DataField]
    public List<SWBattlePassQuest> DailyQuests = new();

    [DataField]
    public List<SWBattlePassQuest> WeeklyQuests = new();

    /// <summary>
    /// How many daily quests a player has at once.
    /// </summary>
    [DataField]
    public int DailyQuestCount = 3;

    public DateTime StartUtc => ParseDay(Start);

    /// <summary>
    /// The first moment after the season.
    /// </summary>
    public DateTime EndUtc => ParseDay(End).AddDays(1);

    private static DateTime ParseDay(string day)
    {
        // Days are Moscow days, as is everything players are told about time.
        var parsed = DateTime.ParseExact(day,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

        return parsed - SWBattlePassRules.MoscowOffset;
    }
}

/// <summary>
/// The XP table of a season. The season design explains why each number is what it is.
/// </summary>
[DataDefinition]
public sealed partial class SWBattlePassXpRules
{
    [DataField]
    public int FighterPerMinute = 5;

    [DataField]
    public int CivilianPerMinute = 3;

    /// <summary>
    /// Active minutes a player can be paid for per day.
    /// </summary>
    [DataField]
    public int MaxMinutesPerDay = 180;

    /// <summary>
    /// Below this many players the minutes pay more, to reward filling an empty server.
    /// </summary>
    [DataField]
    public int SeedBelowPlayers = 10;

    [DataField]
    public float SeedMultiplier = 1.5f;

    [DataField]
    public int FactionTask = 50;

    [DataField]
    public int FactionTaskMinMinutes = 5;

    [DataField]
    public int RoundEnd = 150;

    [DataField]
    public int RoundEndMinMinutes = 15;

    [DataField]
    public int RoundEndMinRoundMinutes = 20;

    [DataField]
    public int Win = 100;

    [DataField]
    public int Kill = 25;

    [DataField]
    public int KillsPerVictim = 2;

    [DataField]
    public int MaxKillXpPerRound = 300;

    [DataField]
    public int FirstRoundOfDay = 300;

    [DataField]
    public int FirstRoundOfDayMinMinutes = 30;

    [DataField]
    public int Tutorial = 500;

    /// <summary>
    /// The XP from a round during a season evening is multiplied by this.
    /// </summary>
    [DataField]
    public float EveningMultiplier = 1.5f;

    /// <summary>
    /// Moscow days of the season evenings.
    /// </summary>
    [DataField]
    public List<DayOfWeek> EveningDays = new() { DayOfWeek.Friday, DayOfWeek.Saturday };

    [DataField]
    public int EveningStartHour = 19;

    [DataField]
    public int EveningEndHour = 23;
}

/// <summary>
/// The two rewards of a tier. Either can be absent.
/// </summary>
[DataDefinition]
public sealed partial class SWBattlePassTier
{
    [DataField]
    public SWBattlePassReward? Free;

    [DataField]
    public SWBattlePassReward? Paid;
}

/// <summary>
/// One reward. Usually one kind of thing; a cosmetic can come with a title.
/// </summary>
[DataDefinition]
public sealed partial class SWBattlePassReward
{
    [DataField]
    public ProtoId<LoadoutPrototype>? Cosmetic;

    /// <summary>
    /// What else the reward opens: the other side's costume of a pair, or the helmet that goes with a uniform.
    /// </summary>
    [DataField]
    public List<ProtoId<LoadoutPrototype>> Also = new();

    [DataField]
    public ProtoId<SWBattlePassCasePrototype>? Case;

    [DataField]
    public int Snowflakes;

    [DataField]
    public LocId? Title;
}

/// <summary>
/// A case: its contents and odds are data, and the window prints them from here.
/// </summary>
[Prototype("swBattlePassCase")]
public sealed partial class SWBattlePassCasePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = string.Empty;

    [DataField(required: true)]
    public LocId Name;

    [DataField(required: true)]
    public SpriteSpecifier Icon = default!;

    /// <summary>
    /// How many cases in a row may give nothing of <see cref="PityRarity"/> or better.
    /// </summary>
    [DataField]
    public int PityAfter = 8;

    /// <summary>
    /// What the guarantee gives. Exactly this rarity, not better, and the odds in the window count on it.
    /// </summary>
    [DataField]
    public SWCosmeticRarity PityRarity = SWCosmeticRarity.Rare;

    /// <summary>
    /// The line a player's very first case gives, whatever the roll, so that it is never just snowflakes.
    /// It does not count toward the guarantee.
    /// </summary>
    [DataField]
    public int? FirstEntry;

    [DataField(required: true)]
    public List<SWBattlePassCaseEntry> Entries = new();
}

/// <summary>
/// A line of a case. Without <see cref="Cosmetic"/> it pays <see cref="Snowflakes"/>.
/// </summary>
[DataDefinition]
public sealed partial class SWBattlePassCaseEntry
{
    /// <summary>
    /// The chance, in percent. The entries of a case add up to 100.
    /// </summary>
    [DataField(required: true)]
    public float Chance;

    [DataField]
    public ProtoId<LoadoutPrototype>? Cosmetic;

    [DataField]
    public SWCosmeticRarity Rarity = SWCosmeticRarity.Common;

    [DataField]
    public int Snowflakes;

    /// <summary>
    /// Snowflakes paid instead of a cosmetic the player already owns.
    /// </summary>
    [DataField]
    public int DuplicateSnowflakes;

    /// <summary>
    /// Price to buy this item directly, so that luck is not the only way to it. Zero means not for sale.
    /// </summary>
    [DataField]
    public int Price;
}

[Serializable, NetSerializable]
public enum SWBattlePassQuestKind : byte
{
    RoundsFinished,
    FactionTasksAlive,
    EnemyKills,
    MinutesOneLife,
    Heals,
    ReturnsAfterDeath,
    Wins,
}

/// <summary>
/// A daily or weekly quest. Its progress only comes from the server's own round tracking.
/// </summary>
[DataDefinition]
public sealed partial class SWBattlePassQuest
{
    [DataField(required: true)]
    public string Id = string.Empty;

    [DataField(required: true)]
    public LocId Name;

    [DataField(required: true)]
    public SWBattlePassQuestKind Kind;

    [DataField]
    public int Target = 1;

    [DataField(required: true)]
    public int Xp;
}
