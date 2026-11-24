using Content.Shared.Preferences.Loadouts;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._SW.Loadouts;

/// <summary>
/// Raised by the loadout window for a closed item. Whoever knows the way to that item can say it better than the item's
/// effect can, and the window need not know what a battle pass is.
/// </summary>
[ByRefEvent]
public record struct SWLoadoutLockHintEvent(ProtoId<LoadoutPrototype> Item)
{
    /// <summary>
    /// What to show instead of the effect's own text.
    /// </summary>
    public FormattedMessage? Hint;

    /// <summary>
    /// Closed items are listed by this, lowest first.
    /// </summary>
    public int Order;
}
