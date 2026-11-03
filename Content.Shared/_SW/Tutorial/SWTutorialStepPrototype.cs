using System.Numerics;
using Content.Shared.Damage;
using Content.Shared.Radio;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._SW.Tutorial;

/// <summary>
/// What the choice screen leads to. A mode without a scenario is shown as not ready yet.
/// </summary>
[Prototype("swTutorialScenario")]
public sealed partial class SWTutorialScenarioPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// The answer on the choice screen that starts this scenario.
    /// </summary>
    [DataField(required: true)]
    public SWTutorialChoice Mode;

    /// <summary>Checkpoint namespace. Increment only when saved exercises are incompatible.</summary>
    [DataField]
    public int Version = 1;

    [DataField]
    public bool GuidedPractice;

    [DataField]
    public string? Map;

    /// <summary>Reconstructed in every private instance, including after a saved checkpoint.</summary>
    [DataField]
    public List<SWTutorialAction> Setup = new();

    /// <summary>
    /// What the student wears from the start.
    /// </summary>
    [DataField(required: true)]
    public ProtoId<StartingGearPrototype> Gear;

    /// <summary>
    /// The steps in order. A mode built from the same steps only needs another list here.
    /// </summary>
    [DataField(required: true)]
    public List<ProtoId<SWTutorialStepPrototype>> Steps = new();
}

/// <summary>
/// One step of the tutorial. The client reads what to show, the server runs the rest.
/// </summary>
[Prototype("swTutorialStep")]
public sealed partial class SWTutorialStepPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// The one line on the screen. Null shows only the exit button.
    /// </summary>
    [DataField]
    public LocId? Prompt;

    /// <summary>
    /// Teaching controls unlocked by this step. Unlocks accumulate; alerts, chat, guidebook, escape/settings,
    /// admin help and unknown upstream widgets retain their normal visibility.
    /// </summary>
    [DataField]
    public List<SWTutorialHudElement> Show = new();

    /// <summary>
    /// The action buttons to leave on the actions bar. Empty leaves them all.
    /// </summary>
    [DataField]
    public List<EntProtoId> ShowActions = new();

    /// <summary>
    /// What flashes from the start of the step.
    /// </summary>
    [DataField]
    public SWTutorialHighlight Highlight;

    /// <summary>
    /// The student cannot fall into a critical state during this step.
    /// </summary>
    [DataField]
    public bool Protected = true;

    /// <summary>
    /// A share of the damage the student takes. A real squad of droids kills in seconds, and a fight teaches nothing then.
    /// </summary>
    [DataField]
    public float DamageTaken = 1f;

    /// <summary>
    /// Shows the button that returns the student after the death scene.
    /// </summary>
    [DataField]
    public bool ReturnButton;

    /// <summary>A resumable exercise boundary whose OnStart restores its finite learning resources.</summary>
    [DataField]
    public bool Checkpoint;

    /// <summary>
    /// What this step cannot be done without. If it is not in the hands, the prompt says so instead of the step's own,
    /// which would talk about a thing the student no longer holds.
    /// </summary>
    [DataField]
    public SWTutorialRequiredItem? RequiresHeld;

    [DataField]
    public List<SWTutorialAction> OnStart = new();

    [DataField(required: true)]
    public SWTutorialCondition Completion = default!;

    /// <summary>
    /// What to do right now inside the step, picked from the state of the world. The last beat whose conditions all
    /// hold is the one on screen, so dropping an item leads back to picking it up with no extra data.
    /// </summary>
    [DataField]
    public List<SWTutorialBeat> Beats = new();

    /// <summary>
    /// Help that comes right after a known mistake.
    /// </summary>
    [DataField]
    public List<SWTutorialMistakeHint> Mistakes = new();
}

[Serializable, NetSerializable]
public enum SWTutorialHudElement : byte
{
    /// <summary>
    /// The hands and the item status, without the slots next to them.
    /// </summary>
    Hands,

    /// <summary>
    /// The pockets and other slots next to the hands.
    /// </summary>
    Slots,
    Actions,
    Alerts,
    Chat,
    Inventory,

    /// <summary>
    /// The whole HUD, as in a normal round.
    /// </summary>
    All,
}

[Serializable, NetSerializable]
public enum SWTutorialHighlight : byte
{
    None,
    CombatAction,
    PocketSlot,
    ReturnButton,
    ActiveHand,
    OtherHand,
    ChatInput,
    ApcBreaker,
    LatheMode,
}

[Serializable, NetSerializable]
public enum SWTutorialMistake : byte
{
    /// <summary>
    /// Clicked a tagged entity from too far away.
    /// </summary>
    TooFar,

    /// <summary>
    /// Clicked a tagged item with the active hand full.
    /// </summary>
    HandBusy,

    /// <summary>
    /// Clicked a tagged entity outside of combat mode.
    /// </summary>
    NoCombatMode,

    /// <summary>
    /// Finished the target with a gun held in one hand.
    /// </summary>
    OneHanded,

    /// <summary>
    /// Shot at an ally.
    /// </summary>
    HitAlly,

    /// <summary>
    /// Spoke, but not on the radio.
    /// </summary>
    NotOnRadio,
}

[DataDefinition]
public sealed partial class SWTutorialBeat
{
    /// <summary>
    /// All have to hold. Empty always holds, which makes the first beat the fallback.
    /// </summary>
    [DataField]
    public List<SWTutorialCondition> When = new();

    /// <summary>
    /// One short line. The world and the HUD show the rest. Null shows no line at all.
    /// </summary>
    [DataField]
    public LocId? Hint;

    [DataField]
    public SWTutorialFocus? Focus;

    [DataField]
    public SWTutorialHighlight Highlight;

    /// <summary>
    /// The student only has to wait here, so the time does not count as being stuck.
    /// </summary>
    [DataField]
    public bool Wait;

    /// <summary>
    /// The four movement keys over the character, until the first step.
    /// </summary>
    [DataField]
    public bool MoveKeys;
}

/// <summary>
/// Where the trail on the floor and the pulsing brackets point: a tagged entity, or a named point of the map.
/// </summary>
[DataDefinition]
public sealed partial class SWTutorialFocus
{
    [DataField]
    public string? Tag;

    [DataField]
    public string? Point;
}

[DataDefinition]
public sealed partial class SWTutorialRequiredItem
{
    [DataField]
    public string? Tag;

    [DataField]
    public EntProtoId? Proto;

    [DataField(required: true)]
    public LocId Prompt;
}

[DataDefinition]
public sealed partial class SWTutorialMistakeHint
{
    [DataField(required: true)]
    public SWTutorialMistake Mistake;

    /// <summary>
    /// The tagged entity the mistake is about. Only used by the click mistakes.
    /// </summary>
    [DataField]
    public string? Tag;

    [DataField(required: true)]
    public LocId Prompt;
}

/// <summary>
/// Something the server does at the start of a step. Plain data: <c>SWTutorialSystem</c> runs it.
/// </summary>
[ImplicitDataDefinitionForInheritors]
public abstract partial class SWTutorialAction;

/// <summary>
/// Spawns one entity at every point with this name.
/// </summary>
public sealed partial class SWTutorialSpawnAction : SWTutorialAction
{
    /// <summary>Local offset from the authored anchor; never a hidden campaign destination.</summary>
    [DataField] public Vector2 Offset;

    [DataField(required: true)]
    public EntProtoId Proto;

    [DataField(required: true)]
    public string Point = string.Empty;

    /// <summary>
    /// The name that conditions and later actions find the entity by.
    /// </summary>
    [DataField]
    public string? Tag;

    /// <summary>
    /// Deletes the entity when the step ends. For arrows.
    /// </summary>
    [DataField]
    public bool UntilStepEnd;
}

public sealed partial class SWTutorialMoveAction : SWTutorialAction
{
    [DataField(required: true)]
    public string Tag = string.Empty;

    [DataField(required: true)]
    public string Point = string.Empty;
}

public sealed partial class SWTutorialDeleteAction : SWTutorialAction
{
    [DataField(required: true)]
    public string Tag = string.Empty;
}

/// <summary>
/// Hurts the student with a popup and remembers the damage for <see cref="SWTutorialHealedCondition"/>.
/// </summary>
public sealed partial class SWTutorialDamageStudentAction : SWTutorialAction
{
    [DataField(required: true)]
    public DamageSpecifier Damage = default!;

    [DataField]
    public LocId? Popup;
}

/// <summary>
/// Lets go of a wielded gun and makes the free hand active, so the student can take something.
/// </summary>
public sealed partial class SWTutorialFreeHandAction : SWTutorialAction;

/// <summary>
/// Clears the hands and puts the tagged gun in them, wielded. The fight has no prompts and tests shooting, not juggling
/// an empty injector and a gun between two hands.
/// </summary>
public sealed partial class SWTutorialRearmAction : SWTutorialAction
{
    [DataField(required: true)]
    public string Tag = string.Empty;
}

/// <summary>
/// The death scene. The body really is dead, without a ghost that could fly to the real round.
/// </summary>
public sealed partial class SWTutorialKillStudentAction : SWTutorialAction
{
    [DataField]
    public LocId? Popup;
}

public sealed partial class SWTutorialReviveStudentAction : SWTutorialAction
{
    [DataField(required: true)]
    public string Point = string.Empty;
}

/// <summary>
/// Orders the tagged droids to come for the student.
/// </summary>
public sealed partial class SWTutorialSendDroidsAction : SWTutorialAction
{
    [DataField(required: true)]
    public string Tag = string.Empty;
}

/// <summary>
/// When a step is done. Plain data: <c>SWTutorialSystem</c> checks it.
/// </summary>
[ImplicitDataDefinitionForInheritors]
public abstract partial class SWTutorialCondition;

public sealed partial class SWTutorialReachCondition : SWTutorialCondition
{
    [DataField(required: true)]
    public string Point = string.Empty;

    [DataField]
    public float Range = 1.5f;
}

/// <summary>
/// The student holds a tagged item, or an item of this prototype.
/// </summary>
public sealed partial class SWTutorialHoldingCondition : SWTutorialCondition
{
    [DataField]
    public string? Tag;

    [DataField]
    public EntProtoId? Proto;
}

public sealed partial class SWTutorialCombatModeCondition : SWTutorialCondition;

/// <summary>
/// The tagged items left the hands and lie at a point or by another tagged entity, or a machine took them.
/// </summary>
public sealed partial class SWTutorialPlacedCondition : SWTutorialCondition
{
    [DataField(required: true)]
    public string Tag = string.Empty;

    [DataField]
    public string? Point;

    [DataField]
    public string? NearTag;

    [DataField]
    public float Range = 1.5f;
}

/// <summary>
/// The opposite of another condition.
/// </summary>
public sealed partial class SWTutorialNotCondition : SWTutorialCondition
{
    [DataField(required: true)]
    public SWTutorialCondition Condition = default!;
}

/// <summary>
/// The tagged item is in the active hand, not just in some hand.
/// </summary>
public sealed partial class SWTutorialActiveHoldingCondition : SWTutorialCondition
{
    [DataField(required: true)]
    public string Tag = string.Empty;
}

/// <summary>
/// The tagged crate stands open.
/// </summary>
public sealed partial class SWTutorialBoxOpenCondition : SWTutorialCondition
{
    [DataField(required: true)]
    public string Tag = string.Empty;
}

/// <summary>
/// The student has the window of the tagged entity open.
/// </summary>
public sealed partial class SWTutorialUiOpenCondition : SWTutorialCondition
{
    [DataField(required: true)]
    public string Tag = string.Empty;
}

/// <summary>
/// The tagged lathe is not on pause.
/// </summary>
public sealed partial class SWTutorialLatheRunningCondition : SWTutorialCondition
{
    [DataField(required: true)]
    public string Tag = string.Empty;
}

/// <summary>
/// The tagged item is held in both hands.
/// </summary>
public sealed partial class SWTutorialWieldedCondition : SWTutorialCondition
{
    [DataField(required: true)]
    public string Tag = string.Empty;
}

/// <summary>
/// Every tagged entity is destroyed or dead.
/// </summary>
public sealed partial class SWTutorialGoneCondition : SWTutorialCondition
{
    [DataField(required: true)]
    public string Tag = string.Empty;

    /// <summary>
    /// This tagged gun has to be wielded at that moment. If not, this counts as a mistake and <see cref="Retry"/> runs.
    /// </summary>
    [DataField]
    public string? WieldedTag;

    [DataField]
    public List<SWTutorialAction> Retry = new();
}

/// <summary>
/// The student has less damage than after the last <see cref="SWTutorialDamageStudentAction"/>, or has just emptied
/// this injector. A drug takes seconds to start working, and the step should answer the press of the key.
/// </summary>
public sealed partial class SWTutorialHealedCondition : SWTutorialCondition
{
    [DataField]
    public EntProtoId? Injector;

    [DataField]
    public string Solution = "pen";
}

/// <summary>
/// The student spoke on this channel. In this fork the semicolon means the Common channel, which no faction key has, so
/// it is not enough that a channel was picked: <c>;text</c> only whispers.
/// </summary>
public sealed partial class SWTutorialSaidOnRadioCondition : SWTutorialCondition
{
    [DataField(required: true)]
    public ProtoId<RadioChannelPrototype> Channel;
}

/// <summary>
/// Every tagged enemy is dead, the student is down, or the time is up: the fight ends either way.
/// </summary>
public sealed partial class SWTutorialFightOverCondition : SWTutorialCondition
{
    [DataField(required: true)]
    public string Tag = string.Empty;

    [DataField]
    public TimeSpan Timeout = TimeSpan.FromSeconds(90);
}

public sealed partial class SWTutorialReturnPressedCondition : SWTutorialCondition;
