using System.Linq;
using Content.Server._SW.ModularSaber;
using Content.Server.Antag;
using Content.Server.Antag.Components;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules;
using Content.Server.Mind;
using Content.Shared._SW.ModularSaber;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Body.Events;
using Content.Shared.GameTicking;
using static Content.Shared.GameTicking.SharedGameTicker;
using Content.Shared.GameTicking.Components;
using Content.Shared.Ghost;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Objectives.Components;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Robust.Server.Player;
using Robust.Shared.Containers;
using Robust.Shared.Audio;
using Robust.Shared.Enums;
using Robust.Shared.Player;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Utility;

namespace Content.Server._SW.Cultist;

[RegisterComponent]
public sealed partial class SWCultistGameRuleComponent : Component
{
    [DataField] public HashSet<ProtoId<JobPrototype>> EligibleJobs = new();
    [DataField] public HashSet<ProtoId<JobPrototype>> TargetJobs = new();
    [DataField] public Dictionary<ProtoId<JobPrototype>, EntProtoId> CommandIds = new();
    [DataField] public Dictionary<ProtoId<JobPrototype>, EntProtoId> LegacySwords = new();
    [DataField] public HashSet<EntProtoId> ModularSwords = new();
    [DataField] public EntProtoId DeliverySword;
    [DataField] public EntProtoId MindRole;
    [DataField] public EntProtoId SelectionTemplate;
    [DataField] public EntProtoId MainObjective;
    [DataField] public EntProtoId SurvivalObjective;
    [DataField] public SoundSpecifier BriefingSound = default!;
    [DataField] public int SelectionMinSeconds = 300;
    [DataField] public int SelectionMaxSeconds = 600;
    [DataField] public int NoCultistWeight = 50;
    [DataField] public int OneCultistWeight = 45;
    [DataField] public int TwoCultistWeight = 5;
    public readonly List<(EntityUid Mind, EntityUid Body)> PendingAutomaticRoles = new();
    public bool AutomaticSelectionPending;
    public TimeSpan SelectionAt;
    public bool SelectionDone;
    public readonly List<(EntityUid Mind, EntityUid Body)> Members = new();
    public EntityUid? TargetBody;
    public bool ConfirmedTargetGib;
    public EntityUid? TargetItem;
    public bool ObjectiveAssigned;
    public string ObjectiveText = string.Empty;
    public bool ItemObjective;
    public readonly HashSet<EntityUid> Delivered = new();
    public readonly HashSet<EntityUid> Cancelled = new();
    public readonly Dictionary<EntityUid, EntityUid> Attempts = new();
    public readonly Dictionary<EntityUid, HashSet<EntityUid>> AttemptParts = new();
    public readonly Dictionary<EntityUid, ContainerSlot> AttemptSlots = new();
    public readonly Dictionary<EntityUid, TimeSpan> NextAttempt = new();
    public SWCultistManualPlan? ManualPlan;
}

/// <summary>Server-only state for retrying a single explicit test assignment without rerolling or duplicating objectives.</summary>
public sealed class SWCultistManualPlan
{
    public EntityUid Target;
    public EntityUid Owner;
    public bool ItemObjective;
    public string Text = string.Empty;
    public bool TargetPrepared;
    public EntityUid? MainObjective;
    public EntityUid? SurvivalObjective;
    public bool Published;
    public bool Uncertain;
    public TimeSpan RetryAt;
    public readonly HashSet<EntityUid> Briefed = new();
    public readonly List<(EntityUid Mind, EntityUid Body)> Unresolved = new();
}

// This marker follows only the specific physical body chosen for the objective.
[RegisterComponent]
public sealed partial class SWCultistTargetBodyComponent : Component
{
    public EntityUid Rule;
}

[RegisterComponent]
public sealed partial class SWCultistObjectiveComponent : Component
{
    public EntityUid Rule;
    public bool Survival;
}

// Track the last physical body without evaluating the objective during play.
[RegisterComponent]
public sealed partial class SWCultistBodyTrackerComponent : Component
{
    public EntityUid Body;
    public bool ConfirmedDeath;
}

[RegisterComponent]
public sealed partial class SWCultistTrackedBodyComponent : Component
{
    public EntityUid Mind;
}
