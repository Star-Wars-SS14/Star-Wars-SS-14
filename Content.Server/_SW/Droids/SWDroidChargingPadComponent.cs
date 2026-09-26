using Content.Shared.Whitelist;

namespace Content.Server._SW.Droids;

[RegisterComponent]
public sealed partial class SWDroidChargingPadComponent : Component
{
    [DataField]
    public float ChargeRate = 30f;

    [DataField]
    public float PassiveDraw = 1f;

    [DataField]
    public float Range = 0.6f;

    [DataField]
    public float UpdateDelay = 0.5f;

    [DataField]
    public EntityWhitelist? Whitelist;

    [ViewVariables]
    public float Accumulator;
}
