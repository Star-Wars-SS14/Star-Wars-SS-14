using System.Globalization;

namespace Content.Shared._SW.BattlePass;

/// <summary>
/// The arithmetic of the pass, shared so that the window and the server cannot disagree.
/// </summary>
public static class SWBattlePassRules
{
    public static readonly TimeSpan MoscowOffset = TimeSpan.FromHours(3);

    /// <summary>
    /// The tier reached with this XP. Zero until the first tier's price is reached.
    /// </summary>
    public static int TierOf(SWBattlePassSeasonPrototype season, int xp)
    {
        return Math.Min(season.Tiers.Count, Math.Max(0, xp) / season.XpPerTier);
    }

    /// <summary>
    /// XP needed to reach a tier.
    /// </summary>
    public static int XpForTier(SWBattlePassSeasonPrototype season, int tier)
    {
        return tier * season.XpPerTier;
    }

    /// <summary>
    /// How many overtime cases this XP has earned past the last tier.
    /// </summary>
    public static int OvertimeEarned(SWBattlePassSeasonPrototype season, int xp)
    {
        var over = xp - XpForTier(season, season.Tiers.Count);
        return over <= 0 || season.OvertimeCase == null ? 0 : over / season.XpPerTier;
    }

    /// <summary>
    /// The key of a claimed reward as it is saved.
    /// </summary>
    public static string RewardKey(int tier, SWBattlePassTrack track)
    {
        return track == SWBattlePassTrack.Paid ? $"{tier}p" : $"{tier}f";
    }

    public static SWBattlePassReward? GetReward(SWBattlePassSeasonPrototype season, int tier, SWBattlePassTrack track)
    {
        if (tier < 1 || tier > season.Tiers.Count)
            return null;

        var entry = season.Tiers[tier - 1];
        return track == SWBattlePassTrack.Paid ? entry.Paid : entry.Free;
    }

    /// <summary>
    /// Whether a reward can be claimed with these numbers, for the window and for the server's check alike.
    /// </summary>
    public static bool CanClaim(
        SWBattlePassSeasonPrototype season,
        int xp,
        bool paid,
        IReadOnlySet<string> claimed,
        int tier,
        SWBattlePassTrack track)
    {
        return GetReward(season, tier, track) != null
            && tier <= TierOf(season, xp)
            && (track == SWBattlePassTrack.Free || paid)
            && !claimed.Contains(RewardKey(tier, track));
    }

    public static int CountClaimable(SWBattlePassSeasonPrototype season, int xp, bool paid, IReadOnlySet<string> claimed)
    {
        var count = 0;
        for (var tier = 1; tier <= season.Tiers.Count; tier++)
        {
            if (CanClaim(season, xp, paid, claimed, tier, SWBattlePassTrack.Free))
                count++;
            if (CanClaim(season, xp, paid, claimed, tier, SWBattlePassTrack.Paid))
                count++;
        }

        return count;
    }

    /// <summary>
    /// Whether a case line resets the guarantee: a cosmetic of the promised rarity or better.
    /// </summary>
    public static bool ResetsPity(SWBattlePassCasePrototype proto, SWBattlePassCaseEntry entry)
    {
        return entry.Cosmetic != null && entry.Rarity >= proto.PityRarity;
    }

    /// <summary>
    /// Whether the guarantee can give this line.
    /// </summary>
    public static bool IsPityEntry(SWBattlePassCasePrototype proto, SWBattlePassCaseEntry entry)
    {
        return entry.Cosmetic != null && entry.Rarity == proto.PityRarity;
    }

    /// <summary>
    /// The real share of each case line over many openings, in percent. The chance on a line is true of one ordinary
    /// case, but the guarantee moves the totals, so the window prints both. A cycle runs up to and including the first
    /// line that resets the guarantee, and at most <see cref="SWBattlePassCasePrototype.PityAfter"/> + 1 cases.
    /// </summary>
    public static float[] LongRunShares(SWBattlePassCasePrototype proto)
    {
        var miss = 0.0;
        var pityTotal = 0.0;
        foreach (var entry in proto.Entries)
        {
            if (!ResetsPity(proto, entry))
                miss += entry.Chance / 100.0;
            if (IsPityEntry(proto, entry))
                pityTotal += entry.Chance;
        }

        // Expected misses and natural hits in one cycle.
        var misses = 0.0;
        for (var i = 1; i <= proto.PityAfter; i++)
        {
            misses += Math.Pow(miss, i);
        }

        var forced = Math.Pow(miss, proto.PityAfter);
        var natural = 1.0 - forced;
        var length = 1.0 + misses;

        var shares = new float[proto.Entries.Count];
        for (var i = 0; i < shares.Length; i++)
        {
            var entry = proto.Entries[i];
            var perCycle = ResetsPity(proto, entry)
                ? natural * (entry.Chance / 100.0) / (1.0 - miss)
                : misses * (entry.Chance / 100.0) / miss;

            if (IsPityEntry(proto, entry))
                perCycle += forced * entry.Chance / pityTotal;

            shares[i] = (float) (100.0 * perCycle / length);
        }

        return shares;
    }

    /// <summary>
    /// <see cref="LongRunShares"/> as the window prints it: one decimal, and the column adds up to exactly 100.
    /// </summary>
    public static float[] ShownLongRunShares(SWBattlePassCasePrototype proto)
    {
        var shares = LongRunShares(proto);
        var tenths = new int[shares.Length];
        var order = new int[shares.Length];
        var sum = 0;
        for (var i = 0; i < shares.Length; i++)
        {
            tenths[i] = (int) Math.Floor(shares[i] * 10f);
            order[i] = i;
            sum += tenths[i];
        }

        Array.Sort(order, (a, b) => (shares[b] * 10f - tenths[b]).CompareTo(shares[a] * 10f - tenths[a]));
        for (var i = 0; sum < 1000 && i < order.Length; i++, sum++)
        {
            tenths[order[i]]++;
        }

        var shown = new float[shares.Length];
        for (var i = 0; i < shown.Length; i++)
        {
            shown[i] = tenths[i] / 10f;
        }

        return shown;
    }

    /// <summary>
    /// The Moscow calendar day of a moment.
    /// </summary>
    public static DateTime MoscowDay(DateTime utc)
    {
        return (utc + MoscowOffset).Date;
    }

    public static string DayKey(DateTime utc)
    {
        return MoscowDay(utc).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The key of the Monday-based week of a moment.
    /// </summary>
    public static string WeekKey(DateTime utc)
    {
        // ISO 8601 by hand: System.Globalization.ISOWeek is not on the client sandbox whitelist.
        // A week belongs to the year of its Thursday and is numbered from that year's first Thursday.
        var day = MoscowDay(utc);
        var weekday = ((int) day.DayOfWeek + 6) % 7; // Monday 0 .. Sunday 6
        var thursday = day.AddDays(3 - weekday);
        var week = (thursday.DayOfYear - 1) / 7 + 1;
        return $"{thursday.Year}-W{week:00}";
    }

    public static bool IsEvening(SWBattlePassXpRules rules, DateTime utc)
    {
        var moscow = utc + MoscowOffset;
        return rules.EveningDays.Contains(moscow.DayOfWeek)
            && moscow.Hour >= rules.EveningStartHour
            && moscow.Hour < rules.EveningEndHour;
    }
}
