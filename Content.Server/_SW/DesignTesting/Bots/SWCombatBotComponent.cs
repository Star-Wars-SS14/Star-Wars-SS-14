using Content.Shared.Roles;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._SW.DesignTesting.Bots;

/// <summary>
/// A soldier NPC for design tests: wears the kit of a faction job, shoots enemies with
/// <c>SWAutoTargetingGun</c> and walks to an objective while no enemy is in sight.
/// Bots do no tasks and use no consoles.
/// </summary>
[RegisterComponent, Access(typeof(SWCombatBotSystem))]
public sealed partial class SWCombatBotComponent : Component
{
    /// <summary>
    /// The faction entity prototype the bot fights for, such as <c>RepublicFaction</c>.
    /// </summary>
    [DataField(required: true)]
    public EntProtoId Faction;

    /// <summary>
    /// The faction whose base and towers the bot attacks.
    /// </summary>
    [DataField(required: true)]
    public EntProtoId EnemyFaction;

    /// <summary>
    /// The job whose starting gear, ID card and faction membership the bot gets.
    /// </summary>
    [DataField(required: true)]
    public ProtoId<JobPrototype> Job;

    /// <summary>
    /// Where the bot goes while no enemy is in sight.
    /// </summary>
    [DataField]
    public SWBotObjective Objective = SWBotObjective.EnemyBase;

    /// <summary>
    /// The target of <see cref="SWBotObjective.Point"/>.
    /// </summary>
    [DataField]
    public MapCoordinates? Point;

    /// <summary>
    /// How long an empty magazine takes to replace. Bots get an endless supply of magazines.
    /// </summary>
    [DataField]
    public TimeSpan ReloadTime = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How often the bot re-checks its gun, enemies and objective.
    /// </summary>
    [DataField]
    public TimeSpan ThinkInterval = TimeSpan.FromSeconds(0.5);

    /// <summary>
    /// How often the objective position is recomputed; towers get destroyed.
    /// </summary>
    [DataField]
    public TimeSpan ObjectiveInterval = TimeSpan.FromSeconds(5);

    [ViewVariables]
    public TimeSpan NextThink;

    [ViewVariables]
    public TimeSpan NextObjectiveUpdate;

    [ViewVariables]
    public TimeSpan? ReloadEnd;

    [ViewVariables]
    public bool Armed;

    [ViewVariables]
    public EntityUid? Gun;

    [ViewVariables]
    public EntProtoId? MagazinePrototype;

    [ViewVariables]
    public MapCoordinates? ObjectivePosition;

    /// <summary>
    /// The end of the current leg towards <see cref="ObjectivePosition"/>.
    /// </summary>
    [ViewVariables]
    public MapCoordinates? Waypoint;

    /// <summary>
    /// The objective currently written to the NPC blackboard; null while the bot holds to fight.
    /// </summary>
    [ViewVariables]
    public MapCoordinates? MovingTo;
}

public enum SWBotObjective : byte
{
    /// <summary>
    /// Stay and fight whoever comes.
    /// </summary>
    Hold,

    /// <summary>
    /// The enemy faction's task console.
    /// </summary>
    EnemyBase,

    /// <summary>
    /// Halfway between the own and the enemy task console.
    /// </summary>
    Centre,

    /// <summary>
    /// The nearest communication tower.
    /// </summary>
    Towers,

    /// <summary>
    /// <see cref="SWCombatBotComponent.Point"/>.
    /// </summary>
    Point,
}
