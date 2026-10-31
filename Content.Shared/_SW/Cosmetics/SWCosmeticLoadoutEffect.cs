using System.Diagnostics.CodeAnalysis;
using Content.Shared.CCVar;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Players.PlayTimeTracking;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Preferences.Loadouts.Effects;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._SW.Cosmetics;

/// <summary>
/// Only a player who owns this loadout can pick it, and only while the cosmetic season is on.
/// A loadout with this effect must stay purely visual. <c>SWCosmeticSalesTest</c> enforces that.
/// </summary>
public sealed partial class SWCosmeticLoadoutEffect : LoadoutEffect
{
    /// <summary>
    /// Whether the item is only worn during the cosmetic season. Battle pass rewards stay wearable all year.
    /// </summary>
    [DataField]
    public bool Seasonal = true;

    /// <summary>
    /// Species without this slot. The item would otherwise be left on the floor at spawn.
    /// </summary>
    [DataField]
    public List<ProtoId<SpeciesPrototype>> BlockedSpecies = new();

    /// <summary>
    /// What a player who does not own it is told, when it does not come from the sale.
    /// </summary>
    [DataField]
    public LocId? NotOwnedText;

    public override bool Validate(
        HumanoidCharacterProfile profile,
        RoleLoadout loadout,
        LoadoutPrototype proto,
        ICommonSession? session,
        IDependencyCollection collection,
        [NotNullWhen(false)] out FormattedMessage? reason)
    {
        reason = null;

        if (BlockedSpecies.Contains(profile.Species))
        {
            reason = FormattedMessage.FromUnformatted(Loc.GetString("sw-battlepass-cosmetic-species-blocked"));
            return false;
        }

        var cfg = collection.Resolve<IConfigurationManager>();
        var now = DateTime.UtcNow;

        // No session means nobody to check ownership for, and that must not hand out a paid item.
        var owned = session != null
            && SWCosmeticTrackers.IsOwned(collection.Resolve<ISharedPlaytimeManager>().GetPlayTimes(session), proto.ID);

        if (owned && (!Seasonal || SWCosmeticSeason.IsVisible(cfg, now)))
            return true;

        if (!owned && NotOwnedText is { } notOwned)
        {
            reason = FormattedMessage.FromUnformatted(Loc.GetString(notOwned));
            return false;
        }

        string text;
        if (owned)
        {
            var from = SWCosmeticSeason.FormatDay(cfg.GetCVar(SWVars.CosmeticVisibleFrom));
            text = Loc.GetString("sw-cosmetic-owned-off-season", ("date", from));
        }
        else
        {
            text = Loc.GetString(SWCosmeticSeason.IsSaleOpen(cfg, now) ? "sw-cosmetic-not-owned" : "sw-cosmetic-sale-closed");
        }

        // Unformatted: the owner writes these texts, and a stray bracket must not throw during validation.
        reason = FormattedMessage.FromUnformatted(text);
        return false;
    }
}
