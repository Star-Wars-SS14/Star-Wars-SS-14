using Robust.Shared.Serialization;

namespace Content.Shared._SW.Tutorial;

/// <summary>Requires the real item to be inside the specified physical container.</summary>
public sealed partial class SWTutorialContainedCondition : SWTutorialCondition
{
    [DataField(required: true)] public string Tag = default!;
    [DataField(required: true)] public string ContainerTag = default!;
}

/// <summary>Requires output observed after the exercise's actual SWLathe production cycle.</summary>
public sealed partial class SWTutorialProducedCondition : SWTutorialCondition;

/// <summary>Requires the exercise's real powered receiver and enabled APC breaker.</summary>
public sealed partial class SWTutorialPoweredCondition : SWTutorialCondition
{
    [DataField] public string Tag = "machine";
    [DataField] public string SupplyTag = "power";
}

/// <summary>Requires a real magazine inserted into the held exercise weapon, with usable ammunition.</summary>
public sealed partial class SWTutorialLoadedCondition : SWTutorialCondition
{
    [DataField] public string Weapon = "weapon";
    [DataField] public string Magazine = "magazine";
}

/// <summary>Requires an actual hit fired while wielding the exercise weapon and a defeated named target.</summary>
public sealed partial class SWTutorialDefeatedCondition : SWTutorialCondition
{
    [DataField] public string Tag = "target";
    [DataField] public List<SWTutorialAction> Retry = new();
}

/// <summary>Requires a measured reduction of the prepared patient's real damage.</summary>
public sealed partial class SWTutorialPatientHealedCondition : SWTutorialCondition
{
    [DataField] public string Tag = "patient";
    [DataField] public float Amount = 10;
}

/// <summary>Prepares a finite private medical exercise; never damages campaign entities.</summary>
public sealed partial class SWTutorialDamagePatientAction : SWTutorialAction
{
    [DataField] public string Tag = "patient";
    [DataField(required: true)] public Content.Shared.Damage.DamageSpecifier Damage = default!;
}
