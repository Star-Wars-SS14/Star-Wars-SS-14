using Robust.Shared.Prototypes;

namespace Content.Shared._SW._Senate.Ministries;

[Prototype("swSecurityCase")]
public sealed partial class SWSecurityCasePrototype : IPrototype
{
    [IdDataField] public string ID { get; } = default!;
    [DataField] public string Title = string.Empty;
    [DataField] public SWSecurityCaseKind Kind;
    [DataField] public int Weight = 1;
    [DataField] public string Briefing = string.Empty;
    [DataField] public List<SWSecurityCaseStage> Stages = new();
    [DataField] public string Success = string.Empty;
    [DataField] public string Failure = string.Empty;
    [DataField] public string Extraction = string.Empty;
    [DataField] public string Sabotage = string.Empty;

    /// <summary>Reject incomplete stories and placeholders that cannot be revealed at that point.</summary>
    public bool IsValid()
    {
        if (string.IsNullOrWhiteSpace(Title) || Weight <= 0 || Stages.Count != 3
            || Kind is not (SWSecurityCaseKind.Domestic or SWSecurityCaseKind.Imperial)
            || !ValidText(Briefing, false) || !ValidText(Failure, false)) return false;
        if (Kind == SWSecurityCaseKind.Domestic ? !ValidText(Success, false)
            : !ValidText(Extraction, false) || !ValidText(Sabotage, false)) return false;
        for (var i = 0; i < Stages.Count; i++)
        {
            var stage = Stages[i];
            if (stage == null || !ValidText(stage.Scene, false) || !ValidText(stage.Objective, false)
                || !ValidText(stage.Clue, i + 1 < Stages.Count)) return false;
        }
        return true;
    }

    private static bool ValidText(string text, bool allowNext)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var withoutKnown = text.Replace("{world}", "").Replace("{originWorld}", "").Replace("{sector}", "");
        if (allowNext) withoutKnown = withoutKnown.Replace("{nextWorld}", "");
        return !withoutKnown.Contains('{') && !withoutKnown.Contains('}');
    }
}

[DataDefinition]
public sealed partial class SWSecurityCaseStage
{
    [DataField] public string Scene = string.Empty;
    [DataField] public string Objective = string.Empty;
    [DataField] public string Clue = string.Empty;
}
