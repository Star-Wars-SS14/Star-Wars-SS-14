using Robust.Shared;
using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

/// <summary>
/// Contains all the CVars used by content.
/// </summary>
/// <remarks>
/// NOTICE FOR FORKS: Put your own CVars in a separate file with a different [CVarDefs] attribute. RT will automatically pick up on it.
/// </remarks>
[CVarDefs]
public sealed partial class SWVars : CVars
{
    /*
     * DISCORD
     */
    /// <summary>
    ///     The discord **FORUM** channel id that admin messages are sent to. If it's not a forum channel, everything will explode.
    /// </summary>
    public static readonly CVarDef<string> AdminAhelpRelayChannelId =
        CVarDef.Create("discord.ahelp_relay_channel_id", string.Empty, CVar.SERVERONLY);

    /// <summary>
    ///     If this is true, the ahelp relay shows that the response was from discord. If this is false, all messages an admin sends will be shown as if the admin was ingame.
    /// </summary>
    public static readonly CVarDef<bool> AdminAhelpRelayShowDiscord =
        CVarDef.Create("discord.ahelp_relay_show_discord", true, CVar.SERVERONLY);

    /*
     * DESIGN TESTING
     */

    /// <summary>
    ///     Speeds up SW round timers for design tests: task start delay and add interval, the faction task
    ///     pipeline, the respawn delay, the end-round check and vote timers, the ore refill.
    ///     10 makes them 10 times shorter. Combat, movement and the engine tick rate are not affected.
    ///     Multiplied with the speedup of an active <c>SWTimerSpeedupGamerule</c> (the <c>SWTestPreset</c>).
    /// </summary>
    public static readonly CVarDef<float> TimerSpeedup =
        CVarDef.Create("sw.timer_speedup", 1f, CVar.SERVERONLY);

    /*
     * TUTORIAL
     */

    /// <summary>
    ///     The choice screen is offered on entry to players with less overall play time than this, in minutes.
    /// </summary>
    public static readonly CVarDef<float> TutorialOfferMinutes =
        CVarDef.Create("sw.tutorial_offer_minutes", 15f, CVar.SERVERONLY);

    /// <summary>
    ///     How long a disconnected student keeps the private tutorial map, in seconds.
    /// </summary>
    public static readonly CVarDef<float> TutorialDisconnectTimeout =
        CVarDef.Create("sw.tutorial_disconnect_timeout", 300f, CVar.SERVERONLY);

    /// <summary>
    ///     The map every student gets a copy of. Any map with the points the scenario asks for works.
    /// </summary>
    public static readonly CVarDef<string> TutorialMap =
        CVarDef.Create("sw.tutorial_map", "/Maps/SW/Tutorial/factory.yml", CVar.SERVERONLY);

    /// <summary>
    ///     How many students can be on their maps at once. Each entry costs a map load inside a server tick.
    /// </summary>
    public static readonly CVarDef<int> TutorialMaxStudents =
        CVarDef.Create("sw.tutorial_max_students", 8, CVar.SERVERONLY);

    /// <summary>
    ///     A graduate with no soldier slot left gets one extra slot, on the side with fewer living.
    /// </summary>
    public static readonly CVarDef<bool> TutorialGraduateExtraSlot =
        CVarDef.Create("sw.tutorial_graduate_extra_slot", true, CVar.SERVERONLY);

    /*
     * TERMINAL UI
     */

    /// <summary>
    ///     Draws the whole interface in the SW terminal style. Off gives the plain Nanotrasen look at once.
    /// </summary>
    public static readonly CVarDef<bool> UITerminal =
        CVarDef.Create("sw.ui_terminal", true, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Colours the lines of the whole terminal interface with the local player's faction after spawning.
    ///     Off by default: the colour then belongs to the consoles that a faction owns.
    /// </summary>
    public static readonly CVarDef<bool> UIPlayerAccent =
        CVarDef.Create("sw.ui_player_accent", false, CVar.CLIENTONLY | CVar.ARCHIVE);

    /*
     * BATTLE PASS
     */

    /// <summary>
    ///     Switches earning XP on or off. Claims and the window keep working either way.
    /// </summary>
    public static readonly CVarDef<bool> BattlePassEnabled =
        CVarDef.Create("sw.battlepass_enabled", true, CVar.SERVERONLY);

    /// <summary>
    ///     Pretends this moment is now, for tests of season dates, for example <c>2027-01-10T18:00:00</c>.
    ///     Empty means the real time.
    /// </summary>
    public static readonly CVarDef<string> BattlePassNow =
        CVarDef.Create("sw.battlepass_now", string.Empty, CVar.SERVERONLY);

    /// <summary>
    ///     The first day that counts for the catch-up bank, <c>yyyy-MM-dd</c>, Moscow time. Set it to the day of the
    ///     release: the season's date lies before it, and days nobody could play must not double everyone's XP.
    ///     Empty means no bank at all.
    /// </summary>
    public static readonly CVarDef<string> BattlePassCatchUpFrom =
        CVarDef.Create("sw.battlepass_catchup_from", string.Empty, CVar.SERVERONLY);

    /// <summary>
    ///     Opens a season before its start date. On in development builds through the build config preset, so that
    ///     the pass can be played before its real date; production follows the calendar.
    /// </summary>
    public static readonly CVarDef<bool> BattlePassPreseasonOpen =
        CVarDef.Create("sw.battlepass_preseason_open", false, CVar.SERVERONLY);

    /// <summary>
    ///     How many players must be on the server for minutes in a round to count.
    /// </summary>
    public static readonly CVarDef<int> BattlePassMinPlayers =
        CVarDef.Create("sw.battlepass_min_players", 2, CVar.SERVERONLY);

    /*
     * COSMETICS
     */

    /// <summary>
    ///     Whether owned cosmetics can be worn: "auto" follows the visible dates, "on" and "off" force it.
    /// </summary>
    public static readonly CVarDef<string> CosmeticSeason =
        CVarDef.Create("sw.cosmetic_season", "auto", CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     First day cosmetics can be worn, "MM-dd", every year.
    /// </summary>
    public static readonly CVarDef<string> CosmeticVisibleFrom =
        CVarDef.Create("sw.cosmetic_visible_from", "12-01", CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     Last day cosmetics can be worn, "MM-dd", every year.
    /// </summary>
    public static readonly CVarDef<string> CosmeticVisibleUntil =
        CVarDef.Create("sw.cosmetic_visible_until", "02-29", CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     First day of the sale, "yyyy-MM-dd". It only changes the text a player without the item reads.
    /// </summary>
    public static readonly CVarDef<string> CosmeticSaleFrom =
        CVarDef.Create("sw.cosmetic_sale_from", "2026-12-15", CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     Last day of the sale, "yyyy-MM-dd".
    /// </summary>
    public static readonly CVarDef<string> CosmeticSaleUntil =
        CVarDef.Create("sw.cosmetic_sale_until", "2027-01-15", CVar.SERVER | CVar.REPLICATED);
}
