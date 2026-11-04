using Robust.Shared.Map;
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
    Restart,
    Production,
    InfantryMedicine,
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
public sealed class SWTutorialChoiceEvent(SWTutorialChoice choice, bool restart = false) : EntityEventArgs
{
    public readonly SWTutorialChoice Choice = choice;
    public readonly bool Restart = restart;
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
    SWTutorialHighlight highlight = SWTutorialHighlight.None,
    LocId? note = null,
    Guid attempt = default,
    int revision = 0,
    SWTutorialHudElement[]? unlocked = null,
    int beat = 0,
    NetEntity? focusEntity = null,
    NetCoordinates? focusCoordinates = null) : EntityEventArgs
{
    public readonly int Beat = beat;

    /// <summary>
    /// What the trail on the floor leads to. An entity follows the thing; coordinates mark a place.
    /// </summary>
    public readonly NetEntity? FocusEntity = focusEntity;
    public readonly NetCoordinates? FocusCoordinates = focusCoordinates;

    public readonly Guid Attempt = attempt;
    public readonly int Revision = revision;
    public readonly SWTutorialHudElement[] Unlocked = unlocked ?? [];
    public readonly bool Active = active;
    public readonly ProtoId<SWTutorialStepPrototype>? Step = step;
    public readonly LocId? Prompt = prompt;
    public readonly SWTutorialHighlight Highlight = highlight;

    /// <summary>
    /// What went wrong a moment ago. It stays until the next step, so it can be read at any pace.
    /// </summary>
    public readonly LocId? Note = note;
}

/// <summary>
/// Server to client: the scenario is finished, show what comes next.
/// </summary>
[Serializable, NetSerializable]
public sealed class SWTutorialFinishedEvent(bool roundStarted, int republicAlive, int empireAlive, bool campaign = false) : EntityEventArgs
{
    public readonly bool RoundStarted = roundStarted;
    public readonly int RepublicAlive = republicAlive;
    public readonly int EmpireAlive = empireAlive;

    /// <summary>A campaign offers civilian entry; other modes offer a side with a soldier slot.</summary>
    public readonly bool Campaign = campaign;
}

/// <summary>
/// Client to server: a graduate picked a side on the finish screen.
/// </summary>
[Serializable, NetSerializable]
public sealed class SWTutorialJoinSideEvent(SWTutorialSide side) : EntityEventArgs
{
    public readonly SWTutorialSide Side = side;
}

/// <summary>Bound to a specific scene revision: retries and late packets cannot affect the next exercise.</summary>
[Serializable, NetSerializable]
public sealed class SWTutorialHelpEvent(Guid attempt, int revision, bool retry = false) : EntityEventArgs
{
    public readonly Guid Attempt = attempt;
    public readonly int Revision = revision;
    public readonly bool Retry = retry;
}
