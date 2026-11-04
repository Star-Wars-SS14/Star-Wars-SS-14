using System.Linq;
using System.Threading.Tasks;
using Content.Server.Administration.Logs;
using Content.Server.Database;
using Content.Server.Players.PlayTimeTracking;
using Content.Shared._SW.Cosmetics;
using Content.Shared.Database;
using Content.Shared.Preferences.Loadouts;
using Robust.Server.Player;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server._SW.Cosmetics;

/// <summary>
/// Ownership of cosmetics. It never handles money: prices and payment data are not stored anywhere.
/// </summary>
public sealed class SWCosmeticSystem : EntitySystem
{
    [Dependency] private readonly IAdminLogManager _adminLog = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IServerDbManager _db = default!;
    [Dependency] private readonly PlayTimeTrackingManager _playTime = default!;

    /// <summary>
    /// Grants or revokes a cosmetic, for players online or offline, and writes it to the admin log.
    /// The database comes first, so nothing is told a grant worked when it did not persist.
    /// </summary>
    public async Task SetOwned(
        NetUserId user,
        string userName,
        ProtoId<LoadoutPrototype> cosmetic,
        bool owned,
        string adminName)
    {
        var tracker = SWCosmeticTrackers.ForLoadout(cosmetic);
        var value = owned ? SWCosmeticTrackers.Owned : TimeSpan.Zero;

        await _db.UpdatePlayTimes([new PlayTimeUpdate(user, tracker, value)]);

        if (_player.TryGetSessionById(user, out var session)
            && _playTime.TryGetTrackerTimes(session, out var times))
        {
            _playTime.AddTimeToTracker(session, tracker, value - times.GetValueOrDefault(tracker));
            _playTime.QueueSendTimers(session);
        }

        var action = owned ? "granted" : "revoked";
        _adminLog.Add(LogType.AdminCommands,
            LogImpact.High,
            $"{adminName} {action} cosmetic {cosmetic.Id} for {userName} ({user})");
    }

    /// <summary>
    /// Which cosmetics a player owns, online or offline.
    /// </summary>
    public async Task<List<string>> GetOwned(NetUserId user)
    {
        var times = await _db.GetPlayTimes(user);
        return times
            .Where(t => t.Tracker.StartsWith(SWCosmeticTrackers.Prefix) && t.TimeSpent > TimeSpan.Zero)
            .Select(t => t.Tracker[SWCosmeticTrackers.Prefix.Length..])
            .ToList();
    }
}
