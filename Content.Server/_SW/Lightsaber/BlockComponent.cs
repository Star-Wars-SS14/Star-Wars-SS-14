using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;

namespace Content.Server._SW.SWLightsaber;

/// <summary>
/// This is used for...
/// </summary>
[RegisterComponent]
public sealed partial class BlockComponent : Component
{
    [AutoNetworkedField]
    public bool IsCooldownBlocking;

    [DataField("blockingToggleAction", customTypeSerializer: typeof(PrototypeIdSerializer<EntityPrototype>))]
    public string BlockingAction = "ActionBlockLightSaber";

    [DataField, AutoNetworkedField]
    public EntityUid? BlockingActionEntity;

    [DataField("blockSound")] public SoundSpecifier BlockSound =
        new SoundPathSpecifier("/Audio/Weapons/block_metal1.ogg")
        {
            Params = AudioParams.Default.WithVariation(0.25f)
        };

    [DataField("BlockActiveTime")]
    public double BlockActiveTime = 5f;

    [DataField("BlockCooldown")]
    public double BlockCooldown = 15f;
}

[RegisterComponent]
public sealed partial class BlockEventComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? BlockingActionEntity;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    [AutoNetworkedField]
    public TimeSpan BlockActiveTime;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    [AutoNetworkedField]
    public TimeSpan BlockCooldown;
}

[RegisterComponent]
public sealed partial class BlockCooldownComponent : Component
{
}
