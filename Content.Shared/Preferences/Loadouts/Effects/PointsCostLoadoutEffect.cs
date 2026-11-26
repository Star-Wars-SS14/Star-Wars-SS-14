using System.Diagnostics.CodeAnalysis;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared.Preferences.Loadouts.Effects;

public sealed partial class PointsCostLoadoutEffect : LoadoutEffect
{
    [DataField(required: true)]
    public int Cost = 1;

    public override bool Validate(
        HumanoidCharacterProfile profile,
        RoleLoadout loadout,
        LoadoutPrototype proto, // Corvax-Sponsors
        ICommonSession? session,
        IDependencyCollection collection,
        [NotNullWhen(false)] out FormattedMessage? reason)
    {
        reason = null;
        var protoManager = collection.Resolve<IPrototypeManager>();

        if (!protoManager.TryIndex(loadout.Role, out var roleProto) || roleProto.Points == null)
        {
            return true;
        }

        //SW start
        // Upstream said <= here, so the last point could not be spent, and showed the key instead of a text.
        if (loadout.Points < Cost)
        {
            reason = FormattedMessage.FromUnformatted(Loc.GetString("sw-loadout-points-short", ("cost", Cost), ("left", loadout.Points ?? 0)));
            return false;
        }
        //SW end

        return true;
    }

    public override void Apply(RoleLoadout loadout)
    {
        loadout.Points -= Cost;
    }
}
