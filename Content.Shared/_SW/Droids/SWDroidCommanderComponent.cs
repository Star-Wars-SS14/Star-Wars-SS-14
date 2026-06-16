using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;

namespace Content.Shared._SW.Droids;

[RegisterComponent]
public sealed partial class SWDroidCommanderComponent : Component
{
    [DataField(customTypeSerializer: typeof(PrototypeIdSerializer<EntityPrototype>))]
    public string TakeCommandAction = "ActionSWDroidTakeCommand";

    [DataField]
    public EntityUid? TakeCommandActionEntity;

    [DataField(customTypeSerializer: typeof(PrototypeIdSerializer<EntityPrototype>))]
    public string OrderFollowAction = "ActionSWDroidOrderFollow";

    [DataField]
    public EntityUid? OrderFollowActionEntity;

    [DataField(customTypeSerializer: typeof(PrototypeIdSerializer<EntityPrototype>))]
    public string OrderStayAction = "ActionSWDroidOrderStay";

    [DataField]
    public EntityUid? OrderStayActionEntity;

    [DataField]
    public SWDroidOrderType CurrentOrder = SWDroidOrderType.Follow;

    [DataField]
    public Dictionary<SWDroidOrderType, string> OrderCallouts = new()
    {
        { SWDroidOrderType.Stay, "SWDroidCommandStay" },
        { SWDroidOrderType.Follow, "SWDroidCommandFollow" }
    };

    public readonly HashSet<EntityUid> Droids = new();
}
