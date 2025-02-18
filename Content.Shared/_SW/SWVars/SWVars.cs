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
    ///     The token used to authenticate with Discord. For the Bot to function set: discord.token, discord.guild_id, and discord.prefix.
    ///     If this is empty, the bot will not connect.
    /// </summary>
    public static readonly CVarDef<string> DiscordToken =
        CVarDef.Create("discord.token", string.Empty, CVar.SERVERONLY | CVar.CONFIDENTIAL);

    /// <summary>
    ///     The Discord guild ID to use for commands as well as for several other features, like the ahelp relay.
    ///     If this is empty, the bot will not connect.
    /// </summary>
    public static readonly CVarDef<string> DiscordGuildId =
        CVarDef.Create("discord.guild_id", string.Empty, CVar.SERVERONLY);

    /// <summary>
    ///     Prefix used for commands for the Discord bot.
    ///     If this is empty, the bot will not connect.
    /// </summary>
    public static readonly CVarDef<string> DiscordPrefix =
        CVarDef.Create("discord.prefix", "!", CVar.SERVERONLY);

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
}
