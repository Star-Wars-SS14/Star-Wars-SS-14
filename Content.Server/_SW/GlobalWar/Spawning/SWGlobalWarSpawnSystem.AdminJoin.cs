using Content.Server.Administration.Managers;
using Content.Shared.Administration;
using Robust.Shared.Player;

namespace Content.Server._SW.GlobalWar.Spawning;

public sealed partial class SWGlobalWarSpawnSystem
{
    [Dependency] private readonly IAdminManager _admins = default!;

    /// <summary>
    /// An active admin may enter again while an earlier character still lives or waits for its return, to debug the
    /// campaign. The earlier body stays in the world without its player; faction locks and slots still apply.
    /// </summary>
    private bool MayJoinOverLife(ICommonSession session)
    {
        return _admins.HasAdminFlag(session, AdminFlags.Admin);
    }
}
