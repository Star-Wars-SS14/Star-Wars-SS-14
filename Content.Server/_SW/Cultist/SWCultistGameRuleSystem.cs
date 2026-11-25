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

public sealed class SWCultistGameRuleSystem : GameRuleSystem<SWCultistGameRuleComponent>
{
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly MindSystem _minds = default!;
    [Dependency] private readonly SharedJobSystem _jobs = default!;
    [Dependency] private readonly AntagSelectionSystem _antags = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly SharedIdCardSystem _idCards = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedStorageSystem _storage = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly MetaDataSystem _metadata = default!;

    // A player has at most one current spawn snapshot; the exact issued entity is retained.
    private readonly record struct IssuedCommandId(EntityUid Mind, EntityUid Body, EntityUid Card, ProtoId<JobPrototype> Job);
    private readonly record struct CultistItemTarget(EntityUid Item, EntityUid Owner, bool IsIdCard);
    private readonly Dictionary<NetUserId, IssuedCommandId> _issuedCommandIds = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWCultistObjectiveComponent, ObjectiveGetProgressEvent>(OnProgress);
        SubscribeLocalEvent<SWCultistTargetBodyComponent, BeingGibbedEvent>(OnTargetGibbed);
        SubscribeLocalEvent<SWCultistGameRuleComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<RoundEndedEvent>(OnRoundEnded);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
    }

    protected override void Started(EntityUid uid, SWCultistGameRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        component.SelectionAt = TimeSpan.FromSeconds(RobustRandom.Next(component.SelectionMinSeconds, component.SelectionMaxSeconds + 1));
    }

    protected override void ActiveTick(EntityUid uid, SWCultistGameRuleComponent component, GameRuleComponent gameRule, float frameTime)
    {
        if (GameTicker.RunLevel != GameRunLevel.InRound)
            return;
        if (!component.SelectionDone && GameTicker.RoundDuration() >= component.SelectionAt)
        {
            component.SelectionDone = true;
            Select(uid, component);
        }
        if (component.AutomaticSelectionPending)
        {
            ResolveAutomaticRoles(uid, component);
            if (component.AutomaticSelectionPending)
                return;
        }
        if (component.ManualPlan is { } manual)
        {
            if (!manual.Published && Timing.CurTime >= manual.RetryAt)
                TryPublishManual(uid, component, manual);
            if (!manual.Published || manual.Uncertain)
                return;
        }
        foreach (var (mind, body) in component.Members)
        {
            if (component.Delivered.Contains(mind) || component.Cancelled.Contains(mind))
                continue;
            if (!FinishAttempt(component, mind))
            {
                if (component.NextAttempt.TryGetValue(mind, out var retry) && Timing.CurTime < retry)
                    continue;
                component.NextAttempt[mind] = Timing.CurTime + TimeSpan.FromSeconds(1);
                try
                {
                    CleanupAttempt(component, mind);
                }
                catch (Exception e)
                {
                    Log.Error($"Could not clean up cultist sword attempt for {mind}: {e}");
                }
                if (!FinishAttempt(component, mind))
                    continue;
            }
            if (TerminatingOrDeleted(body) || TerminatingOrDeleted(mind))
            {
                CancelPending(component, mind);
                continue;
            }
            if (!CanReceive(mind, body) ||
                component.NextAttempt.TryGetValue(mind, out var next) && Timing.CurTime < next)
                continue;
            component.NextAttempt[mind] = Timing.CurTime + TimeSpan.FromSeconds(1);
            TryDeliver(component, mind, body);
        }
    }

    private bool IsAlive(EntityUid body) => !TerminatingOrDeleted(body) &&
        TryComp<MobStateComponent>(body, out var state) && state.CurrentState == MobState.Alive;

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        var user = args.Player.UserId;
        _issuedCommandIds.Remove(user);
        if (!_minds.TryGetMind(args.Mob, out var mind, out var mindComp) ||
            mindComp.OwnedEntity != args.Mob || mindComp.UserId != user)
            return;
        if (args.JobId is not { } job ||
            !_inventory.TryGetContainerSlotEnumerator((args.Mob, null), out var slots, SlotFlags.IDCARD))
            return;
        while (slots.NextItem(out var equipped))
        {
            if (!_idCards.TryGetIdCard(equipped, out var card))
                continue;
            _issuedCommandIds[user] = new IssuedCommandId(mind, args.Mob, card.Owner, job);
            return;
        }
    }

    private List<CultistItemTarget> CollectItemTargets(SWCultistGameRuleComponent rule, HashSet<EntityUid> excludedBodies)
    {
        var result = new List<CultistItemTarget>();
        var seen = new HashSet<EntityUid>();
        foreach (var session in _players.Sessions)
        {
            if (!TryPlayer(session, out var body, out var mind, out var job) || excludedBodies.Contains(body))
                continue;
            var inventory = PersonalInventory(body);
            if (rule.LegacySwords.TryGetValue(job, out var legacy))
            {
                foreach (var item in inventory)
                {
                    if (TryComp<MetaDataComponent>(item, out var meta) &&
                        IsTargetSword(rule, item, meta, legacy) && seen.Add(item))
                        result.Add(new CultistItemTarget(item, body, false));
                }
            }
            if (!rule.CommandIds.TryGetValue(job, out var cardPrototype) ||
                !_issuedCommandIds.TryGetValue(session.UserId, out var issued) ||
                issued.Mind != mind || issued.Body != body || issued.Job != job ||
                TerminatingOrDeleted(issued.Card) || MetaData(issued.Card).EntityPrototype?.ID != cardPrototype.Id ||
                !HasComp<IdCardComponent>(issued.Card) || !inventory.Contains(issued.Card) || !seen.Add(issued.Card))
                continue;
            result.Add(new CultistItemTarget(issued.Card, body, true));
        }
        return result;
    }

    private bool IsTargetSword(SWCultistGameRuleComponent rule, EntityUid item, MetaDataComponent meta, EntProtoId legacy)
    {
        var id = meta.EntityPrototype?.ID;
        if (id == legacy.Id)
            return true;
        if (id == null || !rule.ModularSwords.Contains(new EntProtoId(id)) ||
            !TryComp<SWModularSaberComponent>(item, out var saber))
            return false;
        return saber.Crystal?.Item is { } crystal && HasComp<SWSaberCrystalComponent>(crystal) &&
               saber.Emitter?.Item is { } emitter && HasComp<SWSaberEmitterComponent>(emitter) &&
               saber.Focus?.Item is { } focus && HasComp<SWSaberFocusComponent>(focus);
    }

    private HashSet<EntityUid> PersonalInventory(EntityUid body)
    {
        var visited = new HashSet<EntityUid>();
        if (TerminatingOrDeleted(body))
            return visited;
        var pending = new Stack<EntityUid>(_hands.EnumerateHeld((body, null)));
        if (_inventory.TryGetContainerSlotEnumerator((body, null), out var slots))
            while (slots.NextItem(out var root)) pending.Push(root);
        while (pending.TryPop(out var current))
        {
            if (current == body || TerminatingOrDeleted(current) || !visited.Add(current))
                continue;
            if (!TryComp<ContainerManagerComponent>(current, out var manager))
                continue;
            foreach (var container in _containers.GetAllContainers(current, manager))
                foreach (var child in container.ContainedEntities)
                    pending.Push(child);
        }
        return visited;
    }

    private string ItemObjectiveText(CultistItemTarget target)
    {
        var ownerName = target.IsIdCard && TryComp<IdCardComponent>(target.Item, out var card)
            ? card.FullName ?? Name(target.Owner)
            : Name(target.Owner);
        return Loc.GetString(target.IsIdCard ? "sw-cultist-objective-id-card" : "sw-cultist-objective-sword",
            ("owner", ownerName), ("item", Name(target.Item)));
    }

    private bool TryPlayer(ICommonSession session, out EntityUid body, out EntityUid mind, out ProtoId<JobPrototype> job)
    {
        body = default;
        mind = default;
        job = default;
        if (session.Status != SessionStatus.InGame || session.AttachedEntity is not { } current ||
            GameTicker.PlayerGameStatuses.GetValueOrDefault(session.UserId) != PlayerGameStatus.JoinedGame ||
            HasComp<GhostComponent>(current) || !IsLiving(current) ||
            !_minds.TryGetMind(current, out var mindId, out var mindComp) || mindComp.OwnedEntity != current ||
            mindComp.VisitingEntity != null || mindComp.UserId != session.UserId ||
            !_jobs.MindTryGetJobId(mindId, out var jobId) || jobId == null)
            return false;
        body = current;
        mind = mindId;
        job = jobId.Value;
        return true;
    }

    private void Select(EntityUid uid, SWCultistGameRuleComponent rule)
    {
        var total = rule.NoCultistWeight + rule.OneCultistWeight + rule.TwoCultistWeight;
        if (total <= 0)
            return;
        var roll = RobustRandom.Next(total);
        var count = roll < rule.NoCultistWeight ? 0 : roll < rule.NoCultistWeight + rule.OneCultistWeight ? 1 : 2;
        if (count == 0)
            return;
        var template = Spawn(rule.SelectionTemplate);
        if (!TryComp<AntagSelectionComponent>(template, out var selection) ||
            !_antags.TryGetNextAvailableDefinition((template, selection), out var definition,
                _players.Sessions.Count(session => TryPlayer(session, out _, out _, out _))))
        {
            QueueDel(template);
            return;
        }
        var candidates = _players.Sessions.Where(s => TryPlayer(s, out _, out _, out var job) && rule.EligibleJobs.Contains(job)).ToList();
        RobustRandom.Shuffle(candidates);
        foreach (var session in candidates)
        {
            if (rule.Members.Count >= count)
                break;
            if (!TryPlayer(session, out var body, out var mind, out _))
                continue;
            try
            {
                if (!_antags.TryMakeAntag((template, selection), session, definition.Value))
                    continue;
            }
            catch (Exception e)
            {
                Log.Error($"Automatic cultist assignment for {session.Name} failed after a possible mutation: {e}");
            }
            var actualRole = HasCultistRole(rule, mind);
            if (actualRole == true)
                rule.Members.Add((mind, body));
            else if (actualRole == null)
            {
                rule.PendingAutomaticRoles.Add((mind, body));
                break;
            }
        }
        QueueDel(template);
        rule.AutomaticSelectionPending = true;
        ResolveAutomaticRoles(uid, rule);
    }

    private void ResolveAutomaticRoles(EntityUid uid, SWCultistGameRuleComponent rule)
    {
        for (var i = rule.PendingAutomaticRoles.Count - 1; i >= 0; i--)
        {
            var candidate = rule.PendingAutomaticRoles[i];
            var hasRole = HasCultistRole(rule, candidate.Mind);
            if (hasRole == null)
                continue;
            if (hasRole == true && !rule.Members.Any(member => member.Mind == candidate.Mind))
                rule.Members.Add(candidate);
            rule.PendingAutomaticRoles.RemoveAt(i);
        }
        if (rule.PendingAutomaticRoles.Count != 0)
            return;
        rule.AutomaticSelectionPending = false;
        if (rule.Members.Count != 0)
            AssignObjectives(uid, rule);
    }

    /// <summary>Administrative, explicit test setup. Never replaces an existing team or objective.</summary>
    public bool TryStartManual(IReadOnlyList<ICommonSession> players, EntityUid target, bool itemObjective, out string error)
    {
        error = string.Empty;
        if (GameTicker.RunLevel != GameRunLevel.InRound || players.Count is < 1 or > 2)
        {
            error = Loc.GetString("sw-cultist-test-invalid-round-or-count");
            return false;
        }

        var query = QueryActiveRules();
        if (!query.MoveNext(out var ruleUid, out _, out var rule, out _) ||
            query.MoveNext(out _, out _, out _, out _))
        {
            error = Loc.GetString("sw-cultist-test-rule-unavailable");
            return false;
        }
        if (rule.Members.Count != 0 || rule.ObjectiveAssigned || rule.ManualPlan != null || rule.AutomaticSelectionPending)
        {
            error = Loc.GetString("sw-cultist-test-already-assigned");
            return false;
        }
        if (players.Select(p => p.UserId).Distinct().Count() != players.Count)
        {
            error = Loc.GetString("sw-cultist-test-duplicate");
            return false;
        }

        var chosen = new List<(ICommonSession Session, EntityUid Body, EntityUid Mind)>();
        foreach (var player in players)
        {
            if (!TryPlayer(player, out var body, out var mind, out var job) || !rule.EligibleJobs.Contains(job))
            {
                error = Loc.GetString("sw-cultist-test-invalid-player", ("player", player.Name));
                return false;
            }
            chosen.Add((player, body, mind));
        }
        if (TerminatingOrDeleted(target))
        {
            error = Loc.GetString("sw-cultist-test-invalid-target");
            return false;
        }

        // Use the same candidate builder as the ordinary item objective; do not substitute an item.
        EntityUid? targetOwner = null;
        CultistItemTarget? manualItem = null;
        var excludedBodies = chosen.Select(candidate => candidate.Body).ToHashSet();
        if (itemObjective)
        {
            foreach (var candidate in CollectItemTargets(rule, excludedBodies))
            {
                if (candidate.Item != target)
                    continue;
                manualItem = candidate;
                targetOwner = candidate.Owner;
                break;
            }
        }
        else
        {
            foreach (var session in _players.Sessions)
            {
                if (TryPlayer(session, out var body, out _, out var job) && rule.TargetJobs.Contains(job) &&
                    !excludedBodies.Contains(body) && body == target)
                {
                    targetOwner = body;
                    break;
                }
            }
        }
        if (targetOwner is not { } owner)
        {
            error = Loc.GetString("sw-cultist-test-invalid-target");
            return false;
        }

        var template = Spawn(rule.SelectionTemplate);
        try
        {
            if (!TryComp<AntagSelectionComponent>(template, out var selection) ||
                !_antags.TryGetNextAvailableDefinition((template, selection), out var definition,
                _players.Sessions.Count(session => TryPlayer(session, out _, out _, out _))))
            {
                error = Loc.GetString("sw-cultist-test-selection-unavailable");
                return false;
            }
            foreach (var candidate in chosen)
            {
                if (!_antags.IsSessionValid((template, selection), candidate.Session, definition.Value) ||
                    !_antags.IsEntityValid(candidate.Body, definition.Value))
                {
                    error = Loc.GetString("sw-cultist-test-not-allowed", ("player", candidate.Session.Name));
                    return false;
                }
            }

            // Freeze the explicit objective before any mutation. An uncertain mutation is never retried.
            var text = itemObjective && manualItem is { } item
                ? ItemObjectiveText(item)
                : Loc.GetString("sw-cultist-objective-kill", ("target", Name(target)));
            var previousSelectionDone = rule.SelectionDone;
            var plan = new SWCultistManualPlan { Target = target, Owner = owner, ItemObjective = itemObjective, Text = text };
            rule.ManualPlan = plan;
            rule.SelectionDone = true;
            var assignedAll = true;
            foreach (var candidate in chosen)
            {
                bool accepted;
                try
                {
                    // Explicit admin selection bypasses opt-in and its bundled ban/playtime check.
                    accepted = _antags.TryMakeAntag((template, selection), candidate.Session, definition.Value, checkPref: false);
                }
                catch (Exception e)
                {
                    Log.Error($"Manual cultist assignment for {candidate.Session.Name} failed after a possible mutation: {e}");
                    var actualRole = HasCultistRole(rule, candidate.Mind);
                    if (actualRole == true)
                        rule.Members.Add((candidate.Mind, candidate.Body));
                    else if (actualRole == null)
                    {
                        plan.Uncertain = true;
                        plan.Unresolved.Add((candidate.Mind, candidate.Body));
                    }
                    assignedAll = false;
                    break;
                }
                if (!accepted)
                {
                    assignedAll = false;
                    break;
                }
                var hasRole = HasCultistRole(rule, candidate.Mind);
                if (hasRole != true)
                {
                    // TryMakeAntag's boolean does not certify that its void MakeAntag completed.
                    if (hasRole == null)
                    {
                        plan.Uncertain = true;
                        plan.Unresolved.Add((candidate.Mind, candidate.Body));
                    }
                    assignedAll = false;
                    break;
                }
                rule.Members.Add((candidate.Mind, candidate.Body));
            }
            if (rule.Members.Count == 0 && !plan.Uncertain)
            {
                // A normal pre-mutation refusal leaves the original timer untouched.
                rule.ManualPlan = null;
                rule.SelectionDone = previousSelectionDone;
                error = Loc.GetString("sw-cultist-test-assignment-failed", ("count", 0));
                return false;
            }
            rule.ObjectiveText = text;
            TryPublishManual(ruleUid, rule, plan);
            if (!assignedAll || !plan.Published || plan.Uncertain)
            {
                error = Loc.GetString(plan.Uncertain ? "sw-cultist-test-uncertain" : "sw-cultist-test-pending", ("count", rule.Members.Count));
                return false;
            }
            return true;
        }
        finally
        {
            QueueDel(template);
        }
    }

    private bool? HasCultistRole(SWCultistGameRuleComponent rule, EntityUid mindId)
    {
        try
        {
            if (TerminatingOrDeleted(mindId))
                return false;
            if (!TryComp<MindComponent>(mindId, out var mind))
                return null;
            foreach (var role in mind.MindRoleContainer.ContainedEntities)
            {
                if (!TerminatingOrDeleted(role) && TryComp<MetaDataComponent>(role, out var meta) &&
                    meta.EntityPrototype?.ID == rule.MindRole.Id)
                    return true;
            }
            return false;
        }
        catch (Exception e)
        {
            Log.Error($"Could not inspect cultist role for {mindId}: {e}");
            return null;
        }
    }

    private void TryPublishManual(EntityUid uid, SWCultistGameRuleComponent rule, SWCultistManualPlan plan)
    {
        if (plan.Published)
            return;
        plan.RetryAt = Timing.CurTime + TimeSpan.FromSeconds(1);
        // Retry read-only inspection when the first post-exception role inspection was inconclusive.
        for (var i = plan.Unresolved.Count - 1; i >= 0; i--)
        {
            var candidate = plan.Unresolved[i];
            var hasRole = HasCultistRole(rule, candidate.Mind);
            if (hasRole == null)
                continue;
            if (hasRole == true && !rule.Members.Any(member => member.Mind == candidate.Mind))
                rule.Members.Add(candidate);
            plan.Unresolved.RemoveAt(i);
        }
        // A previously confirmed role can disappear while publication is waiting.
        for (var i = rule.Members.Count - 1; i >= 0; i--)
        {
            var member = rule.Members[i];
            var hasRole = HasCultistRole(rule, member.Mind);
            if (hasRole == true)
                continue;
            rule.Members.RemoveAt(i);
            if (hasRole == null)
                plan.Unresolved.Add(member);
        }
        plan.Uncertain = plan.Unresolved.Count != 0;
        if (plan.Uncertain)
            return;
        if (rule.Members.Count == 0)
        {
            // All previously unreadable outcomes are now known and none has the role.
            plan.Published = true;
            return;
        }
        // A published-looking main objective must not be counted as a win while recovery is incomplete.
        rule.ObjectiveAssigned = false;
        try
        {
            if (!plan.TargetPrepared)
            {
                rule.ItemObjective = plan.ItemObjective;
                if (plan.ItemObjective)
                    rule.TargetItem = plan.Target;
                else
                {
                    // Do not reset a previously observed gib on a publication retry.
                    if (rule.TargetBody == null)
                    {
                        rule.TargetBody = plan.Target;
                        rule.ConfirmedTargetGib = false;
                    }
                    if (!TerminatingOrDeleted(plan.Target))
                        EnsureComp<SWCultistTargetBodyComponent>(plan.Target).Rule = uid;
                }
                plan.TargetPrepared = true;
            }
            plan.MainObjective ??= Spawn(rule.MainObjective);
            var main = plan.MainObjective.Value;
            Comp<SWCultistObjectiveComponent>(main).Rule = uid;
            _metadata.SetEntityName(main, plan.Text);
            _metadata.SetEntityDescription(main, plan.Text);
            plan.SurvivalObjective ??= Spawn(rule.SurvivalObjective);
            var survival = plan.SurvivalObjective.Value;
            var survivalComp = Comp<SWCultistObjectiveComponent>(survival);
            survivalComp.Rule = uid;
            survivalComp.Survival = true;
            _metadata.SetEntityName(survival, Loc.GetString("sw-cultist-objective-survive"));

            foreach (var member in rule.Members)
            {
                if (!TryComp<MindComponent>(member.Mind, out var mind) || HasCultistRole(rule, member.Mind) != true)
                    return;
                if (!mind.Objectives.Contains(main))
                    _minds.AddObjective(member.Mind, mind, main);
                if (!mind.Objectives.Contains(survival))
                    _minds.AddObjective(member.Mind, mind, survival);
            }
            // Both shared entities are attached to every known assigned mind. Never declare victory on uncertain mutation.
            rule.ObjectiveAssigned = !plan.Uncertain;
            plan.Published = true;
            foreach (var member in rule.Members)
            {
                // The notification API has no exactly-once acknowledgement; avoid duplicate messages on exception.
                if (!plan.Briefed.Add(member.Mind))
                    continue;
                try
                {
                    SendCultistBriefing(rule, member, plan.Text);
                }
                catch (Exception e)
                {
                    Log.Error($"Could not send manual cultist briefing to {member.Mind}: {e}");
                }
            }
        }
        catch (Exception e)
        {
            Log.Error($"Manual cultist objectives could not be published; will retry without selecting a new target: {e}");
        }
    }

    private void AssignObjectives(EntityUid uid, SWCultistGameRuleComponent rule)
    {
        var people = new List<(EntityUid Body, EntityUid Mind, ProtoId<JobPrototype> Job)>();
        foreach (var session in _players.Sessions)
        {
            if (TryPlayer(session, out var body, out var mind, out var job) && rule.TargetJobs.Contains(job) &&
                !rule.Members.Any(m => m.Body == body))
                people.Add((body, mind, job));
        }
        var excludedBodies = rule.Members.Select(member => member.Body).ToHashSet();
        var items = CollectItemTargets(rule, excludedBodies);
        var options = new List<bool>();
        if (people.Count > 0) options.Add(false);
        if (items.Count > 0) options.Add(true);
        string text;
        if (options.Count == 0)
        {
            text = Loc.GetString("sw-cultist-objective-unavailable");
        }
        else
        {
            rule.ItemObjective = RobustRandom.Pick(options);
            if (rule.ItemObjective)
            {
                var target = RobustRandom.Pick(items);
                rule.TargetItem = target.Item;
                text = ItemObjectiveText(target);
            }
            else
            {
                var target = RobustRandom.Pick(people);
                TrackTargetBody(uid, rule, target.Body);
                text = Loc.GetString("sw-cultist-objective-kill", ("target", Name(target.Body)));
            }
            rule.ObjectiveAssigned = true;
        }
        PublishObjectives(uid, rule, text);
    }

    private void PublishObjectives(EntityUid uid, SWCultistGameRuleComponent rule, string text)
    {
        rule.ObjectiveText = text;
        var main = Spawn(rule.MainObjective);
        var mainComp = Comp<SWCultistObjectiveComponent>(main);
        mainComp.Rule = uid;
        _metadata.SetEntityName(main, text);
        _metadata.SetEntityDescription(main, text);
        var survival = Spawn(rule.SurvivalObjective);
        var survivalComp = Comp<SWCultistObjectiveComponent>(survival);
        survivalComp.Rule = uid;
        survivalComp.Survival = true;
        _metadata.SetEntityName(survival, Loc.GetString("sw-cultist-objective-survive"));
        foreach (var member in rule.Members)
        {
            if (!TryComp<MindComponent>(member.Mind, out var mind))
            {
                rule.ObjectiveAssigned = false;
                continue;
            }
            _minds.AddObjective(member.Mind, mind, main);
            _minds.AddObjective(member.Mind, mind, survival);
            SendCultistBriefing(rule, member, text);
        }
    }

    private void SendCultistBriefing(SWCultistGameRuleComponent rule, (EntityUid Mind, EntityUid Body) member, string text)
    {
        var partners = rule.Members.Where(x => x.Mind != member.Mind).Select(x => Name(x.Body)).ToArray();
        var partner = partners.Length > 0
            ? Loc.GetString("sw-cultist-partner", ("partner", string.Join(", ", partners)))
            : Loc.GetString("sw-cultist-alone");
        _antags.SendBriefing(member.Body, Loc.GetString("sw-cultist-briefing", ("partner", partner), ("objective", text)), null, rule.BriefingSound);
    }

    private bool InPersonalInventory(EntityUid body, EntityUid item)
    {
        if (TerminatingOrDeleted(body) || TerminatingOrDeleted(item))
            return false;
        var roots = new HashSet<EntityUid>(_hands.EnumerateHeld((body, null)));
        if (_inventory.TryGetContainerSlotEnumerator((body, null), out var slots))
            while (slots.NextItem(out var root)) roots.Add(root);
        var current = item;
        var visited = new HashSet<EntityUid>();
        while (visited.Add(current))
        {
            if (roots.Contains(current)) return true;
            if (!_containers.TryGetContainingContainer(current, out var container)) return false;
            current = container.Owner;
            if (current == body) return false;
        }
        return false;
    }

    private bool CanReceive(EntityUid mindId, EntityUid body)
    {
        return IsAlive(body) && TryComp<MindComponent>(mindId, out var mind) &&
            mind.OwnedEntity == body && mind.VisitingEntity == null &&
            TryComp<ActorComponent>(body, out var actor) && actor.PlayerSession.AttachedEntity == body &&
            mind.UserId == actor.PlayerSession.UserId && actor.PlayerSession.Status == SessionStatus.InGame;
    }

    private void CancelPending(SWCultistGameRuleComponent rule, EntityUid mind)
    {
        // Cancellation blocks new spawns, but never marks the cleanup as completed.
        if (!rule.Delivered.Contains(mind))
            rule.Cancelled.Add(mind);
        try
        {
            CleanupAttempt(rule, mind);
        }
        catch (Exception e)
        {
            // Leave the attempt and slot tracked for the next cleanup event.
            Log.Error($"Could not clean up cultist sword attempt for {mind}: {e}");
        }
    }

    private void CleanupPending(SWCultistGameRuleComponent rule)
    {
        foreach (var member in rule.Members)
            CancelPending(rule, member.Mind);
    }

    protected override void Ended(EntityUid uid, SWCultistGameRuleComponent component, GameRuleComponent gameRule, GameRuleEndedEvent args)
    {
        CleanupPending(component);
    }

    private void OnShutdown(Entity<SWCultistGameRuleComponent> ent, ref ComponentShutdown args) => CleanupPending(ent.Comp);
    private void OnRoundEnded(RoundEndedEvent args) => CleanupAllPending();
    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        CleanupAllPending();
        _issuedCommandIds.Clear();
    }

    private void CleanupAllPending()
    {
        var query = EntityQueryEnumerator<SWCultistGameRuleComponent>();
        while (query.MoveNext(out _, out var rule))
            CleanupPending(rule);
    }

    private bool CloseAttemptSlot(SWCultistGameRuleComponent rule, EntityUid mind)
    {
        if (!rule.AttemptSlots.TryGetValue(mind, out var slot))
            return true;
        // Never shut down an occupied slot: doing so destroys its contents.
        if (slot.ContainedEntity != null)
            return false;
        if (!TerminatingOrDeleted(slot.Owner) &&
            _containers.TryGetContainer(slot.Owner, slot.ID, out var existing) && existing == slot)
            _containers.ShutdownContainer(slot);
        rule.AttemptSlots.Remove(mind);
        return true;
    }

    private bool FinishAttempt(SWCultistGameRuleComponent rule, EntityUid mind)
    {
        if (rule.Attempts.TryGetValue(mind, out var sword) && !Deleted(sword))
            return false;
        if (!CloseAttemptSlot(rule, mind))
            return false;
        rule.Attempts.Remove(mind);
        rule.AttemptParts.Remove(mind);
        return true;
    }

    private bool ControlsAttempt(SWCultistGameRuleComponent rule, EntityUid mind, EntityUid sword) =>
        rule.AttemptSlots.TryGetValue(mind, out var slot) && slot.Contains(sword) ||
        !_containers.TryGetContainingContainer(sword, out _) && Transform(sword).MapUid == null;

    private void CleanupAttempt(SWCultistGameRuleComponent rule, EntityUid mind)
    {
        if (!rule.Attempts.TryGetValue(mind, out var sword) || Deleted(sword))
        {
            FinishAttempt(rule, mind);
            return;
        }
        var member = rule.Members.FirstOrDefault(m => m.Mind == mind);
        if (rule.Delivered.Contains(mind) || InPersonalInventory(member.Body, sword))
        {
            rule.Delivered.Add(mind);
            if (CloseAttemptSlot(rule, mind))
            {
                rule.Attempts.Remove(mind);
                rule.AttemptParts.Remove(mind);
            }
            return;
        }
        if (ControlsAttempt(rule, mind, sword))
        {
            // Delete only parts created with this clone and still in its own slots. The
            // saber releases other contents on termination, and callbacks may move it.
            if (TryComp<SWModularSaberComponent>(sword, out var saber) &&
                rule.AttemptParts.TryGetValue(mind, out var ownedParts))
            {
                foreach (var part in new[] { saber.Crystal?.Item, saber.Emitter?.Item, saber.Focus?.Item })
                {
                    if (!ControlsAttempt(rule, mind, sword))
                        break;
                    if (part is { } uid && ownedParts.Contains(uid) && !Deleted(uid) &&
                        _containers.TryGetContainingContainer(uid, out var container) && container.Owner == sword)
                        Del(uid);
                }
            }
            if (ControlsAttempt(rule, mind, sword))
            {
                Del(sword);
                FinishAttempt(rule, mind);
                return;
            }
        }
        // Do not destroy or duplicate an entity moved by an unexpected callback.
        rule.Cancelled.Add(mind);
        Log.Error($"Cultist sword attempt {sword} moved outside its controlled slot; preserving it and stopping retries");
    }

    private bool HasDeliverySpace(EntityUid body)
    {
        if (_inventory.TryGetContainerSlotEnumerator((body, null), out var pockets, SlotFlags.POCKET))
            while (pockets.MoveNext(out var pocket))
                if (pocket.ContainedEntity == null)
                    return true;
        if (!_inventory.TryGetContainerSlotEnumerator((body, null), out var slots))
            return false;
        while (slots.NextItem(out var item))
            if (HasComp<StorageComponent>(item)) return true;
        return false;
    }

    private void TryDeliver(SWCultistGameRuleComponent rule, EntityUid mind, EntityUid body)
    {
        if (!CanReceive(mind, body) || !HasDeliverySpace(body))
            return;
        var slot = _containers.EnsureContainer<ContainerSlot>(body, $"swCultistDelivery-{mind}", out var existed);
        if (existed)
        {
            rule.Cancelled.Add(mind);
            Log.Error($"Unexpected existing cultist delivery slot on {body}");
            return;
        }
        rule.AttemptSlots.Add(mind, slot);
        try
        {
            var sword = Spawn(rule.DeliverySword);
            rule.Attempts.Add(mind, sword);
            if (TryComp<SWModularSaberComponent>(sword, out var saber))
            {
                var parts = new HashSet<EntityUid>();
                foreach (var part in new[] { saber.Crystal?.Item, saber.Emitter?.Item, saber.Focus?.Item })
                    if (part is { } uid) parts.Add(uid);
                rule.AttemptParts.Add(mind, parts);
            }
            if (!_containers.Insert(sword, slot))
                return;
            if (_inventory.TryGetContainerSlotEnumerator((body, null), out var pockets, SlotFlags.POCKET))
            {
                while (pockets.MoveNext(out var pocket))
                {
                    if (!_inventory.TryEquip(body, sword, pocket.ID, silent: true))
                        continue;
                    rule.Delivered.Add(mind);
                    return;
                }
            }
            if (!_inventory.TryGetContainerSlotEnumerator((body, null), out var slots))
                return;
            while (slots.NextItem(out var item))
            {
                if (TryComp<StorageComponent>(item, out var storage) &&
                    _storage.CanInsert(item, sword, out _, storage) &&
                    _storage.Insert(item, sword, out _, storageComp: storage, playSound: false, stackAutomatically: false))
                {
                    rule.Delivered.Add(mind);
                    return;
                }
            }
        }
        finally
        {
            // No yield: ShowContents is not a secrecy mechanism, so resolve this slot synchronously.
            try
            {
                CleanupAttempt(rule, mind);
            }
            catch (Exception e)
            {
                // Keep the same attempt tracked; a later tick must finish its cleanup before any new spawn.
                Log.Error($"Could not clean up cultist sword attempt for {mind}: {e}");
            }
        }
    }

    private bool IsLiving(EntityUid body) => !TerminatingOrDeleted(body) &&
        TryComp<MobStateComponent>(body, out var state) && state.CurrentState is MobState.Alive or MobState.Critical;

    private bool TryLivingBody(EntityUid mindId, out EntityUid body)
    {
        body = default;
        if (!TryComp<MindComponent>(mindId, out var mind) || mind.OwnedEntity is not { } owned ||
            HasComp<GhostComponent>(owned) || !IsLiving(owned))
            return false;
        body = owned;
        return true;
    }

    private bool Survival(SWCultistGameRuleComponent rule) => rule.Members.Any(m => TryLivingBody(m.Mind, out _));

    private void TrackTargetBody(EntityUid ruleUid, SWCultistGameRuleComponent rule, EntityUid body)
    {
        rule.TargetBody = body;
        rule.ConfirmedTargetGib = false;
        EnsureComp<SWCultistTargetBodyComponent>(body).Rule = ruleUid;
    }

    private void OnTargetGibbed(Entity<SWCultistTargetBodyComponent> ent, ref BeingGibbedEvent args)
    {
        if (TryComp<SWCultistGameRuleComponent>(ent.Comp.Rule, out var rule) && rule.TargetBody == ent.Owner)
            rule.ConfirmedTargetGib = true;
    }

    private bool MainSuccess(SWCultistGameRuleComponent rule)
    {
        if (!rule.ObjectiveAssigned) return false;
        if (rule.ItemObjective)
            return rule.TargetItem is { } item && !TerminatingOrDeleted(item) &&
                   rule.Members.Any(m => TryLivingBody(m.Mind, out var body) && InPersonalInventory(body, item));
        if (rule.TargetBody is not { } target)
            return false;
        if (!TerminatingOrDeleted(target))
            return TryComp<MobStateComponent>(target, out var state) && state.CurrentState == MobState.Dead;
        // A missing body only counts if its destruction was confirmed by BeingGibbedEvent.
        return rule.ConfirmedTargetGib;
    }


    private void OnProgress(Entity<SWCultistObjectiveComponent> objective, ref ObjectiveGetProgressEvent args)
    {
        args.Progress = TryComp<SWCultistGameRuleComponent>(objective.Comp.Rule, out var rule) &&
            (objective.Comp.Survival ? Survival(rule) : MainSuccess(rule)) ? 1 : 0;
    }

    internal void AppendCultistRoundEndText(SWCultistGameRuleComponent component, RoundEndTextAppendEvent args)
    {
        if (component.Members.Count == 0) return;
        var mainSuccess = MainSuccess(component);
        var survival = Survival(component);
        var success = mainSuccess && survival;
        var outcome = Loc.GetString(success ? "sw-cultist-roundend-success" : "sw-cultist-roundend-failure");
        var objective = string.IsNullOrEmpty(component.ObjectiveText)
            ? Loc.GetString("sw-cultist-objective-unavailable")
            : component.ObjectiveText;
        var mainStatus = Loc.GetString(mainSuccess ? "sw-cultist-roundend-completed" : "sw-cultist-roundend-not-completed");
        var survivalStatus = Loc.GetString(survival ? "sw-cultist-roundend-survived" : "sw-cultist-roundend-none-survived");
        args.AddLine($"[color={(success ? "#66ff99" : "#ff7777")}]{FormattedMessage.EscapeText(outcome)}[/color]\n" +
            $"[color=#fff2a8]{FormattedMessage.EscapeText(objective)} — {FormattedMessage.EscapeText(mainStatus)}\n" +
            $"{FormattedMessage.EscapeText(survivalStatus)}[/color]");
    }
}
