using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW.ForceAbilities.SWHealAbility;

[RegisterComponent]
public sealed partial class SWHealAbilityComponent : Component
{
    [DataField("damageContainer")]
    public ProtoId<DamageContainerPrototype>? DamageContainerID;

    [DataField("damageModifierSet")]
    public ProtoId<DamageModifierSetPrototype>? DamageModifierSetId;

    [DataField]
    public List<ProtoId<DamageContainerPrototype>>? DamageContainers;

    [DataField]
    public string HealAction = "SWHealAction";

    [DataField]
    public DamageSpecifier Damage = new DamageSpecifier();

    [DataField]
    public TimeSpan Delay = default!;

    [DataField]
    public SoundSpecifier? HealingBeginSound = null;

    [DataField]
    public SoundSpecifier? HealingEndSound = null;

    [DataField]
    public float BloodlossModifier = 0.0f;

    [DataField]
    public float ModifyBloodLevel = 0.0f;

}
