using Robust.Shared.Prototypes;

namespace Content.Shared._SW.SaberForms;

[Prototype("swSaberForm")]
public sealed partial class SWSaberFormPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name;

    [DataField(required: true)]
    public LocId Description;

    [DataField]
    public EntProtoId? SwingAnimation;

    [DataField]
    public EntProtoId? RiposteSwingAnimation;

    /// <summary>Modifier shared by the wide attack arc and its animation.</summary>
    [DataField]
    public float VisualArcMultiplier = 1f;

    [DataField]
    public float DashArcMultiplier = 1f;

    [DataField]
    public float RiposteWindow;

    [DataField]
    public bool RiposteFromAnyMelee;

    [DataField]
    public float RiposteDamageMultiplier = 1f;

    [DataField]
    public float RiposteAttackRateMultiplier = 1f;

    [DataField]
    public float GuardDurationMultiplier = 1f;

    [DataField]
    public float GuardReflectChanceMultiplier = 1f;

    [DataField]
    public SWSaberDefenseVisual ParryVisual;

    [DataField]
    public SWSaberDefenseVisual ReflectionVisual;

    [DataField]
    public float GuardExitAttackDelay;
}
