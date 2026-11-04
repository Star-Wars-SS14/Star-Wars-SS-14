namespace Content.Server._SW.Gamerules.TimerSpeedup;

/// <summary>
/// While this game rule is active, SW round timers run faster. Used by the hidden test preset.
/// </summary>
[RegisterComponent, Access(typeof(SWTimerSpeedupSystem))]
public sealed partial class SWTimerSpeedupGameruleComponent : Component
{
    /// <summary>
    /// How many times shorter the SW round timers are. Multiplied with the <c>sw.timer_speedup</c> cvar.
    /// </summary>
    [DataField]
    public float Speedup = 10f;
}
