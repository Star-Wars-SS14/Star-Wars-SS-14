using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class SWVars
{
    /// <summary>The look of the in-game HUD: "light" or "console". A player's choice in the options.</summary>
    public static readonly CVarDef<string> HudStyle =
        CVarDef.Create("sw.hud_style", "light", CVar.CLIENTONLY | CVar.ARCHIVE);
}
