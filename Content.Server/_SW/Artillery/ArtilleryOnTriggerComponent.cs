using Content.Shared.Explosion;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;
namespace Content.Server._SW.Artillery;

/// <summary>
/// Upon being triggered will EMP area around it.
/// </summary>
[RegisterComponent]
public sealed partial class ArtilleryOnTriggerComponent : Component
{
    [DataField("tilesRange"), ViewVariables(VVAccess.ReadWrite)]
    public int TilesRange = 10;

    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("explosionType", required: true, customTypeSerializer: typeof(PrototypeIdSerializer<ExplosionPrototype>))]
    public string ExplosionType = default!;

    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("maxIntensity")]
    public float MaxIntensity = 4;

    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("intensitySlope")]
    public float IntensitySlope = 1;

    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("totalIntensity")]
    public float TotalIntensity = 10;

    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("tileBreakScale")]
    public float TileBreakScale = 1f;

    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("maxTileBreak")]
    public int MaxTileBreak = int.MaxValue;

    /// <summary>
    /// How many explosions spawned per time simultaneously
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("explosionsCount")]
    public int ExplosionsCount = 5;

    /// <summary>
    /// How many times explosions will be spawned
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("timesCount")]
    public int TimesCount = 5;

    public int TimesLeft = 5;

    /// <summary>
    /// Time between two times
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("delayBetweenTimes")]
    public float DelayBetweenTimes = 2f;

    public TimeSpan NextTime = default!;

    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("tileRadius")]
    public int TileRadius = 5;

    public bool Enabled = false;
}
