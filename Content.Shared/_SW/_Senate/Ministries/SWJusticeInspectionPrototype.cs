using System;
using System.Collections.Generic;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._SW._Senate.Ministries;

[Prototype("swJusticeInspection")]
public sealed partial class SWJusticeInspectionPrototype : IPrototype
{
    [IdDataField] public string ID { get; } = default!;
    [DataField(required: true)] public string Title = string.Empty;
    [DataField(required: true)] public SWJusticeInstitutionKind Kind;
    [DataField(required: true)] public string Briefing = string.Empty;
    [DataField(required: true)] public List<SWJusticeCheckPrototype> Checks = new();
    [DataField(required: true)] public List<SWJusticeVariantPrototype> Variants = new();
    [DataField] public int Weight = 1;
    [DataField] public int Severity = 1;

    public bool IsValid()
    {
        if (string.IsNullOrWhiteSpace(ID) || !ValidText(Title) || !ValidText(Briefing)
            || !Enum.IsDefined(Kind) || Weight <= 0 || Severity is < 1 or > 3
            || Checks == null || Checks.Count != 3 || Variants == null || Variants.Count != 3)
            return false;
        var checks = new HashSet<string>();
        foreach (var check in Checks)
        {
            if (check == null || string.IsNullOrWhiteSpace(check.Id) || !ValidText(check.Id)
                || !checks.Add(check.Id) || !ValidText(check.Title) || check.Cost is < 100 or > 300
                || !double.IsFinite(check.Hours) || check.Hours is < 0.5 or > 1)
                return false;
        }
        var ids = new HashSet<string>();
        var conclusions = new HashSet<SWJusticeConclusion>();
        foreach (var variant in Variants)
        {
            if (variant == null || string.IsNullOrWhiteSpace(variant.Id) || !ValidText(variant.Id)
                || !ids.Add(variant.Id) || !Enum.IsDefined(variant.Conclusion) || !conclusions.Add(variant.Conclusion)
                || !ValidText(variant.Explanation) || variant.Evidence == null || variant.Evidence.Count != checks.Count)
                return false;
            foreach (var check in checks)
                if (!variant.Evidence.TryGetValue(check, out var evidence) || !ValidText(evidence))
                    return false;
        }
        return conclusions.Count == 3;
    }

    private static bool ValidText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var remainder = text.Replace("{world}", "").Replace("{sector}", "")
            .Replace("{institution}", "").Replace("{amount}", "").Replace("{reference}", "");
        return !remainder.Contains('{') && !remainder.Contains('}');
    }
}

[DataDefinition]
public sealed partial class SWJusticeCheckPrototype
{
    [DataField(required: true)] public string Id = string.Empty;
    [DataField(required: true)] public string Title = string.Empty;
    [DataField(required: true)] public int Cost;
    [DataField(required: true)] public double Hours;
}

[DataDefinition]
public sealed partial class SWJusticeVariantPrototype
{
    [DataField(required: true)] public string Id = string.Empty;
    [DataField(required: true)] public SWJusticeConclusion Conclusion;
    [DataField(required: true)] public Dictionary<string, string> Evidence = new();
    [DataField(required: true)] public string Explanation = string.Empty;
}
