using Content.Shared._SW._Senate.Sectors;
using Robust.Shared.Prototypes;

namespace Content.Shared._SW._Senate;

[RegisterComponent]
public sealed partial class SWSenateSectorComponent : Component
{
    [DataField]
    public ProtoId<SWSectorPrototype>? Sector;
}
