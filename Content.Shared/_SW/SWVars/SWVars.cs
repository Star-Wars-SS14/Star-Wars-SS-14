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
}
