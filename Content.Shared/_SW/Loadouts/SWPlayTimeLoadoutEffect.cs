using System.Diagnostics.CodeAnalysis;
using Content.Shared.CCVar;
using Content.Shared.Players.PlayTimeTracking;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Preferences.Loadouts.Effects;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._SW.Loadouts;

/// <summary>
/// An item that opens after some hours of play. Upstream's job requirement could check this, but what it tells the
/// player is about unlocking a role, which reads wrong on a row in the loadout window.
/// </summary>
public sealed partial class SWPlayTimeLoadoutEffect : LoadoutEffect
{
    /// <summary>
    /// Time needed, in seconds.
    /// </summary>
    [DataField(required: true)]
    public TimeSpan Time;

    /// <summary>
    /// The job's tracker whose hours count. Without it, all play counts.
    /// </summary>
    [DataField]
    public ProtoId<PlayTimeTrackerPrototype>? Tracker;

    public override bool Validate(
        HumanoidCharacterProfile profile,
        RoleLoadout loadout,
        LoadoutPrototype proto,
        ICommonSession? session,
        IDependencyCollection collection,
        [NotNullWhen(false)] out FormattedMessage? reason)
    {
        reason = null;

        // No session means a default filled in at spawn, and there the first item of a group is never locked.
        if (session == null || !collection.Resolve<IConfigurationManager>().GetCVar(CCVars.GameRoleLoadoutTimers))
            return true;

        var tracker = Tracker ?? PlayTimeTrackingShared.TrackerOverall;
        var played = collection.Resolve<ISharedPlaytimeManager>().GetPlayTimes(session).GetValueOrDefault(tracker);
        if (played >= Time)
            return true;

        var hours = (int) Math.Ceiling((Time - played).TotalHours);
        reason = FormattedMessage.FromUnformatted(Loc.GetString(
            Tracker == null ? "sw-loadout-time-short" : "sw-loadout-time-short-job",
            ("hours", hours)));
        return false;
    }
}
