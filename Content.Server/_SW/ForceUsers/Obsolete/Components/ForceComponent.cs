using System.Numerics;
using Content.Shared.Alert;
using Content.Shared.Store;
using Content.Shared.Store.Components;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._SW.Force.Components;

[RegisterComponent]
public partial class ForceComponent : Component
{
    // ActionJediDash was removed in #222 (SWDashAction replaced it), so no base abilities are left.
    public List<string> AbilitiesPrototypes = [];

    [DataField("exp")]
    public int Experience;

    [DataField("currencyProto")]
    public ProtoId<CurrencyPrototype> MasteryCurrencyProto = "MasteryExperience";

    [DataField("addExpCritOrDead")]
    public int AddExperienceCriticalOrDead = 10;

    // Dash
    [DataField]
    public SoundSpecifier DashSound = new SoundPathSpecifier("/Audio/_SW/Abilities/dash.ogg");

    [DataField]
    public float DashDistance = 3F;

    [DataField]
    public float DashSpeed = 2.5F;

    public string? DashEffectEnd = "DashEffectEnd";
}

public sealed partial class MasteryTypeCondition : ListingCondition
{
    [DataField("masteryType", required: true)]
    public string MasteryType;

    public override bool Condition(ListingConditionArgs args)
    {
        if (args.EntityManager.HasComponent<DarkForceComponent>(args.StoreEntity) && MasteryType == "light")
            return false;

        if (args.EntityManager.HasComponent<LightForceComponent>(args.StoreEntity) && MasteryType == "dark")
            return false;

        return true;

    }
}
