using Robust.Shared.Prototypes;
using Robust.Shared.Map;
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

    [DataField(customTypeSerializer: typeof(PrototypeIdSerializer<EntityPrototype>))]
    public string OrderGoToAction = "ActionSWDroidOrderGoTo";

    [DataField]
    public EntityUid? OrderGoToActionEntity;

    [DataField(customTypeSerializer: typeof(PrototypeIdSerializer<EntityPrototype>))]
    public string OrderAttackAction = "ActionSWDroidOrderAttack";

    [DataField]
    public EntityUid? OrderAttackActionEntity;

    [DataField]
    public SWDroidOrderType CurrentOrder = SWDroidOrderType.Follow;

    public EntityCoordinates? CurrentGoToTarget;

    public EntityUid? CurrentAttackTarget;

    [DataField]
    public Dictionary<SWDroidOrderType, string> OrderCallouts = new()
    {
        { SWDroidOrderType.Stay, "SWDroidCommandStay" },
        { SWDroidOrderType.Follow, "SWDroidCommandFollow" },
        { SWDroidOrderType.GoTo, "SWDroidCommandGoTo" },
        { SWDroidOrderType.Attack, "SWDroidCommandAttack" }
    };

    public readonly HashSet<EntityUid> Droids = new();
}
