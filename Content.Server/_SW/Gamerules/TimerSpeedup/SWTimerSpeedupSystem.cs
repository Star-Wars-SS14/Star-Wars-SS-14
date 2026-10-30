using Content.Server.GameTicking.Rules;
using Content.Shared.CCVar;
using Content.Shared.GameTicking.Components;
using Robust.Shared.Configuration;

namespace Content.Server._SW.Gamerules.TimerSpeedup;

/// <summary>
/// Scales SW round timers for design tests. The engine has no time-scale cvar (only <c>net.tickrate</c>),
/// so live play is accelerated here, game-side, by shortening the timers themselves.
/// </summary>
public sealed class SWTimerSpeedupSystem : GameRuleSystem<SWTimerSpeedupGameruleComponent>
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;

    private const float MinSpeedup = 0.01f;

    private float _cvarSpeedup = 1f;

    public override void Initialize()
    {
        base.Initialize();
        Subs.CVar(_cfg, SWVars.TimerSpeedup, value => _cvarSpeedup = value, true);
    }

    /// <summary>
    /// The current speedup: the cvar times the speedup of every active speedup rule.
    /// </summary>
    public float GetSpeedup()
    {
        var speedup = _cvarSpeedup;
        var query = QueryActiveRules();
        while (query.MoveNext(out _, out _, out var comp, out _))
        {
            speedup *= comp.Speedup;
        }

        return Math.Max(speedup, MinSpeedup);
    }

    /// <summary>
    /// Shortens a round timer by the current speedup.
    /// </summary>
    public TimeSpan Scale(TimeSpan time)
    {
        return time / GetSpeedup();
    }

    /// <inheritdoc cref="Scale(TimeSpan)"/>
    public float Scale(float seconds)
    {
        return seconds / GetSpeedup();
    }
}
