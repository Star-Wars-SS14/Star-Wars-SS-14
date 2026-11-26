namespace Content.Shared._SW.Cosmetics;

/// <summary>
/// Who owns a cosmetic is stored as a play-time tracker row. The table already keeps any string per player, the server
/// sends a player their own trackers, and they are loaded before preferences are checked, so ownership needs no
/// database migration. Nothing ever adds time to these trackers. A positive value means owned; zero means revoked,
/// because the table has no way to delete a row.
/// </summary>
public static class SWCosmeticTrackers
{
    public const string Prefix = "SWCosmetic:";

    /// <summary>
    /// The value of an owned tracker. It only has to be above zero.
    /// </summary>
    public static readonly TimeSpan Owned = TimeSpan.FromSeconds(1);

    public static string ForLoadout(string loadoutId)
    {
        return Prefix + loadoutId;
    }

    public static bool IsOwned(IReadOnlyDictionary<string, TimeSpan> trackers, string loadoutId)
    {
        return trackers.TryGetValue(ForLoadout(loadoutId), out var time) && time > TimeSpan.Zero;
    }
}
