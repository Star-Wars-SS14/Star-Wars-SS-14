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
    [DataField("instantEffectToSpawn", required: true)]
    public string? ProtoId = null;

    /// <summary>
    /// How many instant effect spawned per time simultaneously
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("countPerTime")]
    public int CountPerTime = 5;

    /// <summary>
    /// How many times instantEffects will be spawned
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

    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("delay")]
    public float DelayInSeconds = 10f;

    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("bombSound", required: true)]
    public string? BombSound = null;
}
