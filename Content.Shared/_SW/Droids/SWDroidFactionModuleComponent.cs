using Content.Shared.Access;
using Robust.Shared.Prototypes;

namespace Content.Shared._SW.Droids;

[RegisterComponent]
public sealed partial class SWDroidFactionModuleComponent : Component
{
    [DataField(required: true)]
    public string Faction = string.Empty;

    [DataField]
    public List<ProtoId<AccessLevelPrototype>> LockAccess = new();
}
