using Content.Shared._SW.GlobalWar.Politics;
using Robust.Shared.Serialization;

namespace Content.Shared._SW.GlobalWar;

/// <summary>The server's answer to a campaign late-join request. Closing the window is not a confirmation.</summary>
[Serializable, NetSerializable]
public sealed class SWGlobalWarJoinResultEvent : EntityEventArgs
{
    public readonly bool Success;
    public readonly string Message;

    public SWGlobalWarJoinResultEvent(bool success, string message)
    {
        Success = success;
        Message = message;
    }
}

/// <summary>What a dead player may do now. An empty <see cref="Life"/> means there is nothing to return from.</summary>
[Serializable, NetSerializable]
public sealed class SWGlobalWarRespawnStateEvent : EntityEventArgs
{
    public readonly Guid Life;
    public readonly TimeSpan ReadyAt;
    public readonly SWGlobalWarFaction Faction;

    /// <summary>Why the civilian return is closed right now; null while it is open or still counting down.</summary>
    public readonly string? CivilianDenied;

    /// <summary>Whether this life holds a Force place, so the special return is offered at all.</summary>
    public readonly bool ForceCarrier;

    /// <summary>Why the Force return is closed right now; null while it is open or still counting down.</summary>
    public readonly string? ForceDenied;

    public SWGlobalWarRespawnStateEvent(Guid life, TimeSpan readyAt, SWGlobalWarFaction faction,
        string? civilianDenied, bool forceCarrier, string? forceDenied)
    {
        Life = life;
        ReadyAt = readyAt;
        Faction = faction;
        CivilianDenied = civilianDenied;
        ForceCarrier = forceCarrier;
        ForceDenied = forceDenied;
    }
}

/// <summary>An explicit choice to end the named life and continue in a new one. The server decides everything else.</summary>
[Serializable, NetSerializable]
public sealed class SWGlobalWarRespawnRequestEvent : EntityEventArgs
{
    public readonly Guid Life;
    public readonly bool Force;

    public SWGlobalWarRespawnRequestEvent(Guid life, bool force)
    {
        Life = life;
        Force = force;
    }
}

[Serializable, NetSerializable]
public sealed class SWGlobalWarRespawnResultEvent : EntityEventArgs
{
    public readonly bool Success;
    public readonly string Message;

    public SWGlobalWarRespawnResultEvent(bool success, string message)
    {
        Success = success;
        Message = message;
    }
}

/// <summary>One change of owner, for the timeline of the verdict.</summary>
[Serializable, NetSerializable]
public readonly record struct SWGlobalWarWarTick(TimeSpan At, SWGlobalWarFaction Owner);

/// <summary>Campaign verdict for one player, sent after the ordinary round-end summary.</summary>
[Serializable, NetSerializable]
public sealed class SWGlobalWarRoundEndEvent : EntityEventArgs
{
    public readonly SWGlobalWarOutcome Outcome;
    public readonly TimeSpan Duration;
    public readonly SWGlobalWarFaction Side;
    public readonly int RepublicWorlds;
    public readonly int EmpireWorlds;
    public readonly int OtherWorlds;
    public readonly int OwnershipChanges;
    public readonly List<SWGlobalWarWarTick> Timeline;
    /// <summary>Orbits and surfaces when the war was decided at the time limit; null when it was decided otherwise.</summary>
    public readonly int? LimitRepublic;
    public readonly int? LimitEmpire;

    public SWGlobalWarRoundEndEvent(SWGlobalWarOutcome outcome, TimeSpan duration, SWGlobalWarFaction side,
        int republicWorlds, int empireWorlds, int otherWorlds, int ownershipChanges, List<SWGlobalWarWarTick>? timeline = null, int? limitRepublic = null,
        int? limitEmpire = null)
    {
        Outcome = outcome;
        Duration = duration;
        Side = side;
        RepublicWorlds = republicWorlds;
        EmpireWorlds = empireWorlds;
        OtherWorlds = otherWorlds;
        OwnershipChanges = ownershipChanges;
        Timeline = timeline ?? new();
        LimitRepublic = limitRepublic;
        LimitEmpire = limitEmpire;
    }
}
