using Content.Shared._SW._Senate.Ministries;
using Robust.Shared.Prototypes;

namespace Content.Shared._SW._Senate;

[RegisterComponent]
public sealed partial class SWMinistryComponent : Component
{
    [DataField]
    public ProtoId<SWMinistryPrototype>? Ministry;
}
