using Content.Shared._SW.Economy.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared.Echo.GameTicking.Components;

[RegisterComponent]
public sealed partial class CapturableStationComponent : Component
{
    [DataField]
    public string? Fraction = null;

    [DataField]
    public Dictionary<ProtoId<SWCurrencyPrototype>, float> CaptureReward = new Dictionary<ProtoId<SWCurrencyPrototype>, float>
    {
        ["SWReinforcementScore"] = 45000
    };

    [DataField]
    public Dictionary<ProtoId<SWCurrencyPrototype>, float> CaprureRewardPerMinuteControlling = new Dictionary<ProtoId<SWCurrencyPrototype>, float>
    {
        ["SWReinforcementScore"] = 1000
    };

    [DataField]
    public TimeSpan TimeOfNextPayment = TimeSpan.FromSeconds(0);
}
