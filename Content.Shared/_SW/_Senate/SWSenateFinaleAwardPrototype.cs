using Robust.Shared.Prototypes;

namespace Content.Shared._SW._Senate;

public enum SWSenateAwardCondition : byte { CloseVote, AuthoredLaw, AuthoredAmendment, CompletedProject, PassedLaw }
public enum SWSenateAwardRecipient : byte { Project, Author, Law }

[Prototype("swSenateFinaleAward")]
public sealed partial class SWSenateFinaleAwardPrototype : IPrototype
{
    [IdDataField] public string ID { get; } = string.Empty;
    [DataField(required: true)] public SWSenateAwardCondition Condition;
    [DataField(required: true)] public SWSenateAwardRecipient Recipient;
    [DataField] public int MinThreshold = 1;
    [DataField] public int Priority;
    [DataField(required: true)] public string TitleKey = string.Empty;
    [DataField(required: true)] public string FactKey = string.Empty;
    [DataField] public string? TieTitleKey;
    [DataField] public string? TieFactKey;
}
