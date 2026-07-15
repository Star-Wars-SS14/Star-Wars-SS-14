using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Shared._SW.Droids;

[RegisterComponent]
public sealed partial class SWDroidLoyaltyModuleComponent : Component
{
    [DataField]
    public HashSet<ProtoId<JobPrototype>> ProtectedJobs = new();
}
