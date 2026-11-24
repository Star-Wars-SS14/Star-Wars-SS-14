using Content.Shared.Preferences;
using Robust.Shared.Network;

namespace Content.Server._SW.GlobalWar.Politics;

/// <summary>Server snapshot of the character actually created, not the subsequently edited lobby preference.</summary>
[RegisterComponent, Access(typeof(SWGlobalWarAssignmentsSystem))]
public sealed partial class SWGlobalWarCharacterProfileComponent : Component
{
    public Guid Generation;
    public NetUserId User;
    public EntityUid Mind;
    public HumanoidCharacterProfile Profile = default!;
}
