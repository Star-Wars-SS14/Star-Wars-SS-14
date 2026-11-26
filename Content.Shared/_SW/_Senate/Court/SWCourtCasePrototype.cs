using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._SW._Senate.Court;

[Prototype("swCourtCase")]
public sealed partial class SWCourtCasePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public string Name = string.Empty;

    [DataField]
    public SWCourtCaseType CaseType = SWCourtCaseType.SectorVsSector;

    [DataField(required: true)]
    public string Description = string.Empty;

    [DataField]
    public string PlaintiffName = string.Empty;

    [DataField]
    public List<string> ProsecutionHints = new();

    [DataField]
    public List<string> DefenseHints = new();

    [DataField]
    public SWCourtOutcome PlaintiffWin = new();

    [DataField]
    public SWCourtOutcome DefendantWin = new();
}

[DataDefinition]
public sealed partial class SWCourtOutcome
{
    [DataField]
    public string Summary = string.Empty;

    [DataField]
    public Dictionary<string, float> PlaintiffSectorEffects = new();

    [DataField]
    public Dictionary<string, float> DefendantSectorEffects = new();

    [DataField]
    public Dictionary<string, float> PolicyEffects = new();

    [DataField]
    public int PlaintiffBudget;

    [DataField]
    public int DefendantBudget;
}

[Serializable, NetSerializable]
public enum SWCourtCaseType : byte
{
    SectorVsRepublic,
    RepublicVsSector,
    SectorVsSector,
    CitizenVsSector,
    RepublicVsOfficial,
}
