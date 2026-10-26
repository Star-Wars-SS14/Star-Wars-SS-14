using Content.Shared.Stacks;
using Content.Shared.Tools;
using Robust.Shared.Prototypes;

namespace Content.Shared._SW.Droids;

[RegisterComponent]
public sealed partial class SWDroidRepairableComponent : Component
{
    [DataField]
    public ProtoId<StackPrototype> Material = "Steel";

    [DataField]
    public int MaterialAmount = 1;

    [DataField]
    public float DamageValue = -30f;

    [DataField]
    public float FuelCost = 15f;

    [DataField]
    public ProtoId<ToolQualityPrototype> QualityNeeded = "Welding";

    [DataField]
    public float DoAfterDelay = 8f;

    [DataField]
    public bool AutoDoAfter = true;
}
