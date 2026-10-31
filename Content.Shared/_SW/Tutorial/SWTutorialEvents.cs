using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._SW.Tutorial;

/// <summary>
/// Play-time tracker names used as persistent flags. The tracker table already stores any string per player,
/// so the flags need no database migration. The client's playtime window only lists job trackers.
/// </summary>
public static class SWTutorialTrackers
{
    /// <summary>
    /// The player answered the choice screen or started the tutorial.
    /// </summary>
    public const string Seen = "SWTutorialSeen";

    /// <summary>
    /// The player finished the tutorial.
    /// </summary>
    public const string Done = "SWTutorialDone";
}

[Serializable, NetSerializable]
public enum SWTutorialChoice : byte
{
    NewToGame,
    NewToServer,
    Skip,
}

[Serializable, NetSerializable]
public enum SWTutorialSide : byte
{
    Republic,
    Empire,
}

/// <summary>
/// Server to client: show the choice screen. Sent once the player's data has loaded.
/// </summary>
[Serializable, NetSerializable]
public sealed class SWTutorialOfferEvent : EntityEventArgs;

/// <summary>
/// Server to client, on entering the lobby: this player has answered the offer before.
/// The client's list of play times holds only job trackers, so it cannot read the flag itself.
/// </summary>
[Serializable, NetSerializable]
public sealed class SWTutorialSeenEvent : EntityEventArgs;

/// <summary>
/// Client to server: the answer on the choice screen.
/// </summary>
[Serializable, NetSerializable]
public sealed class SWTutorialChoiceEvent(SWTutorialChoice choice) : EntityEventArgs
{
    public readonly SWTutorialChoice Choice = choice;
}

/// <summary>
/// Client to server: the exit button.
/// </summary>
[Serializable, NetSerializable]
public sealed class SWTutorialExitEvent : EntityEventArgs;

/// <summary>
/// Client to server: the button that returns after the death scene.
/// </summary>
[Serializable, NetSerializable]
public sealed class SWTutorialReturnEvent : EntityEventArgs;

/// <summary>
/// Server to client: whether this player is in the tutorial, on what step, and what the prompt says now. It is an
/// event and not a component on the body because it has to arrive before the client loads the game screen, or the
/// full HUD flashes.
/// </summary>
[Serializable, NetSerializable]
public sealed class SWTutorialStateEvent(
    bool active,
    ProtoId<SWTutorialStepPrototype>? step = null,
    LocId? prompt = null,
    bool big = false,
    SWTutorialHighlight highlight = SWTutorialHighlight.None,
    LocId? note = null) : EntityEventArgs
{
    public readonly bool Active = active;
    public readonly ProtoId<SWTutorialStepPrototype>? Step = step;
    public readonly LocId? Prompt = prompt;
    public readonly bool Big = big;
    public readonly SWTutorialHighlight Highlight = highlight;

    /// <summary>
    /// What went wrong a moment ago, under the prompt. It stays until the next step, so it can be read at any pace.
    /// </summary>
    public readonly LocId? Note = note;
}

/// <summary>
/// Server to client: the scenario is finished, show what comes next.
/// </summary>
[Serializable, NetSerializable]
public sealed class SWTutorialFinishedEvent(bool roundStarted, int republicAlive, int empireAlive) : EntityEventArgs
{
    public readonly bool RoundStarted = roundStarted;
    public readonly int RepublicAlive = republicAlive;
    public readonly int EmpireAlive = empireAlive;
}

/// <summary>
/// Client to server: a graduate picked a side on the finish screen.
/// </summary>
[Serializable, NetSerializable]
public sealed class SWTutorialJoinSideEvent(SWTutorialSide side) : EntityEventArgs
{
    public readonly SWTutorialSide Side = side;
}
