using Content.Shared._SW._Senate.Sectors;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._SW._Senate.Ministries;

[Serializable, NetSerializable]
public enum SWJusticeStance : byte
{
    Lenient,
    Balanced,
    Harsh,
}

[Serializable, NetSerializable]
public enum SWCaseStage : byte
{
    Investigation,
    Hearing,
    Verdict,
}

[Serializable, NetSerializable]
public enum SWCaseType : byte
{
    Criminal,
    Corruption,
    Separatism,
    Civil,
    Political,
}

[Serializable, NetSerializable]
public enum SWVerdict : byte
{
    Convict,
    Acquit,
    Dismiss,
}

[Prototype("swJusticeProgram")]
public sealed partial class SWJusticeProgramPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; } = default!;

    [DataField]
    public string Name = string.Empty;

    [DataField]
    public string Description = string.Empty;

    [DataField]
    public Dictionary<ProtoId<SWStatPrototype>, float> PolicyEffects = new();

    [DataField]
    public float BacklogDelta;

    [DataField]
    public int Order;
}
