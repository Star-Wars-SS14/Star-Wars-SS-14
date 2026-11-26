namespace Content.Server._SW._Senate.GameTicking;

[RegisterComponent]
public sealed partial class SWSenateRuleComponent : Component
{
    [DataField]
    public int RoundDays = 3;

    public int StartDay;
    public bool EndTriggered;
}
