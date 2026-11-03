using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._SW.GlobalWar.Politics;

/// <summary>
/// One short line for a role in a running campaign. These roles are received during the round, so the tutorial map
/// cannot teach them.
/// </summary>
[Serializable, NetSerializable]
public enum SWGlobalWarCoachHint : byte
{
    None,
    HeadWait,
    HeadDeclare,
    Route,
    Kit,
    Teleporter,
    Countdown,
}

/// <summary>What the player's role and situation are. <see cref="SWGlobalWarCoachRules.Pick"/> reads nothing else.</summary>
public readonly record struct SWGlobalWarCoachFacts(
    SWGlobalWarRole Role,
    bool AtPeace,
    bool PeaceTimeOver,
    bool AtHelm,
    bool Routed,
    bool HasKit,
    bool ArsenalReady,
    bool CanLeave);

public static class SWGlobalWarCoachRules
{
    private const double LineOnEntry = 60;
    private const double CountdownOnEntry = 10;
    private const double CountdownSteady = 60;
    private const double Repeat = 300;
    private const double RepeatShown = 6;

    public static SWGlobalWarCoachHint Pick(SWGlobalWarCoachFacts facts)
    {
        // Nothing is taught in a fight, and no line comes back once the war has begun.
        if (!facts.AtPeace)
            return SWGlobalWarCoachHint.None;

        if (facts.AtHelm && !facts.Routed)
            return SWGlobalWarCoachHint.Route;

        switch (facts.Role)
        {
            case SWGlobalWarRole.Head:
                return facts.PeaceTimeOver ? SWGlobalWarCoachHint.HeadDeclare : SWGlobalWarCoachHint.HeadWait;

            case SWGlobalWarRole.Soldier:
            case SWGlobalWarRole.JuniorOfficer:
            case SWGlobalWarRole.SeniorOfficer:
                if (facts.PeaceTimeOver)
                    return SWGlobalWarCoachHint.None;
                if (!facts.HasKit && facts.ArsenalReady)
                    return SWGlobalWarCoachHint.Kit;
                return facts.CanLeave ? SWGlobalWarCoachHint.Teleporter : SWGlobalWarCoachHint.Countdown;

            default:
                return SWGlobalWarCoachHint.None;
        }
    }

    /// <summary>
    /// A line stays up long enough to be read and then only comes back now and then, because a player may have chosen
    /// not to do it yet. A countdown is also steady for its last minute.
    /// </summary>
    public static bool IsShown(SWGlobalWarCoachHint hint, double sinceStart, double remaining)
    {
        if (hint is SWGlobalWarCoachHint.HeadWait or SWGlobalWarCoachHint.Countdown)
            return sinceStart < CountdownOnEntry || remaining <= CountdownSteady || remaining % Repeat < RepeatShown;

        return sinceStart < LineOnEntry || sinceStart % Repeat < RepeatShown;
    }

    public static string? LocKey(SWGlobalWarCoachHint hint)
    {
        return hint switch
        {
            SWGlobalWarCoachHint.HeadWait => "sw-global-war-coach-head-wait",
            SWGlobalWarCoachHint.HeadDeclare => "sw-global-war-coach-head-declare",
            SWGlobalWarCoachHint.Route => "sw-global-war-coach-route",
            SWGlobalWarCoachHint.Kit => "sw-global-war-coach-kit",
            SWGlobalWarCoachHint.Teleporter => "sw-global-war-coach-teleporter",
            SWGlobalWarCoachHint.Countdown => "sw-global-war-coach-countdown",
            _ => null,
        };
    }
}

/// <summary>One line from the player's liaison officer.</summary>
[Serializable, NetSerializable]
public sealed class SWGlobalWarAideEvent(string message, EntProtoId officer, SWGlobalWarFaction side, float seconds) : EntityEventArgs
{
    public readonly string Message = message;
    public readonly EntProtoId Officer = officer;
    public readonly SWGlobalWarFaction Side = side;
    public readonly float Seconds = seconds;
}

[Serializable, NetSerializable]
public sealed class SWGlobalWarCoachEvent(SWGlobalWarCoachHint hint, NetCoordinates? focus, TimeSpan? warIn) : EntityEventArgs
{
    public readonly SWGlobalWarCoachHint Hint = hint;

    /// <summary>Coordinates and not an entity, because the target is often too far away for the client to know it.</summary>
    public readonly NetCoordinates? Focus = focus;

    /// <summary>How long until war can be declared, when the line counts down to it.</summary>
    public readonly TimeSpan? WarIn = warIn;
}
