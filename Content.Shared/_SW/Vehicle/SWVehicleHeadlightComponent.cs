using Content.Shared.Actions;
using Robust.Shared.Prototypes;

namespace Content.Shared._SW.Vehicle;

[RegisterComponent]
public sealed partial class SWVehicleHeadlightComponent : Component
{
}

[RegisterComponent]
public sealed partial class SWVehicleHeadlightUserComponent : Component
{
    [DataField]
    public EntProtoId Action = "SWVehicleToggleHeadlightsAction";

    [DataField]
    public EntityUid? ActionEntity;
}

public sealed partial class SWToggleVehicleHeadlightsEvent : InstantActionEvent
{
}
