using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server.GameTicking.Rules.Components;

[RegisterComponent, Access(typeof(StarWarsEmpireRuleSystem))]
public sealed partial class StarWarsEmpireRuleComponent : Component
{
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan? NextRoundEndCheck;

    [DataField]
    public TimeSpan EndCheckDelay = TimeSpan.FromSeconds(30);

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan? CheckRoundEnd;

    [DataField]
    public TimeSpan EndRoundDelay = TimeSpan.FromMinutes(120);
}

[RegisterComponent, Access(typeof(StarWarsEmpireRuleSystem))]
public sealed partial class SWShieldGuardComponent : Component
{

}
