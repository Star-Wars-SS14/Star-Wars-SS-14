using System.Linq;
using Content.Server.Antag;
using Content.Server.Antag.Components;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules;
using Content.Server.Mind;
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
using Content.Shared.Roles.Jobs;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Robust.Server.Player;
using Robust.Shared.Containers;
using Robust.Shared.Audio;
using Robust.Shared.Enums;
using Robust.Shared.Player;

namespace Content.Server._SW.Cultist;

[RegisterComponent]
public sealed partial class SWCultistGameRuleComponent : Component
{
    public TimeSpan SelectionAt;
    public bool SelectionDone;
    public readonly List<(EntityUid Mind, EntityUid Body)> Members = new();
    public EntityUid? TargetBody;
    public bool ConfirmedTargetGib;
    public EntityUid? TargetItem;
    public bool ObjectiveAssigned;
    public bool ItemObjective;
    public readonly HashSet<EntityUid> Delivered = new();
    public readonly HashSet<EntityUid> Cancelled = new();
    public readonly Dictionary<EntityUid, EntityUid> Attempts = new();
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
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedStorageSystem _storage = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly MetaDataSystem _metadata = default!;

    private static readonly HashSet<string> EligibleJobs = new() { "SWCivialian", "SWEmpireSoldier", "SWRepublicSoldier" };
    private static readonly HashSet<string> TargetJobs = new() { "SWJedi", "SWSith", "SWRepublicGeneral", "SWEmpireMoff" };

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWCultistObjectiveComponent, ObjectiveGetProgressEvent>(OnProgress);
        SubscribeLocalEvent<SWCultistTargetBodyComponent, BeingGibbedEvent>(OnTargetGibbed);
        SubscribeLocalEvent<SWCultistGameRuleComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<RoundEndedEvent>(OnRoundEnded);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    protected override void Started(EntityUid uid, SWCultistGameRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        component.SelectionAt = TimeSpan.FromSeconds(RobustRandom.Next(300, 601));
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

    private bool TryPlayer(ICommonSession session, out EntityUid body, out EntityUid mind, out string job)
    {
        body = default;
        mind = default;
        job = string.Empty;
        if (session.Status != SessionStatus.InGame || session.AttachedEntity is not { } current ||
            GameTicker.PlayerGameStatuses.GetValueOrDefault(session.UserId) != PlayerGameStatus.JoinedGame ||
            HasComp<GhostComponent>(current) || !IsLiving(current) ||
            !_minds.TryGetMind(current, out var mindId, out var mindComp) || mindComp.OwnedEntity != current ||
            mindComp.VisitingEntity != null || mindComp.UserId != session.UserId ||
            !_jobs.MindTryGetJobId(mindId, out var jobId) || jobId == null)
            return false;
        body = current;
        mind = mindId;
        job = jobId.Value.Id;
        return true;
    }

    private void Select(EntityUid uid, SWCultistGameRuleComponent rule)
    {
        var roll = RobustRandom.Next(100);
        var count = roll < 50 ? 0 : roll < 95 ? 1 : 2;
        if (count == 0)
            return;
        var template = Spawn("SWCultistSelectionTemplate");
        if (!TryComp<AntagSelectionComponent>(template, out var selection) ||
            !_antags.TryGetNextAvailableDefinition((template, selection), out var definition, 20))
        {
            QueueDel(template);
            return;
        }
        var candidates = _players.Sessions.Where(s => TryPlayer(s, out _, out _, out var job) && EligibleJobs.Contains(job)).ToList();
        RobustRandom.Shuffle(candidates);
        foreach (var session in candidates)
        {
            if (rule.Members.Count >= count)
                break;
            if (!TryPlayer(session, out var body, out var mind, out _) ||
                !_antags.TryMakeAntag((template, selection), session, definition.Value))
                continue;
            rule.Members.Add((mind, body));
        }
        QueueDel(template);
        if (rule.Members.Count == 0)
            return;
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
        if (rule.Members.Count != 0 || rule.ObjectiveAssigned || rule.ManualPlan != null)
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
            if (!TryPlayer(player, out var body, out var mind, out var job) || !EligibleJobs.Contains(job))
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

        // Match the ordinary objective pool and bind to this exact body or item.
        EntityUid? targetOwner = null;
        foreach (var session in _players.Sessions)
        {
            if (!TryPlayer(session, out var body, out _, out var job) || !TargetJobs.Contains(job) ||
                chosen.Any(candidate => candidate.Body == body))
                continue;
            if (!itemObjective && target == body)
            {
                targetOwner = body;
                break;
            }
            var expected = job == "SWJedi" ? "SWEnergySwordJedi" : job == "SWSith" ? "SWEnergySwordSith" : null;
            if (itemObjective && expected != null && MetaData(target).EntityPrototype?.ID == expected &&
                InPersonalInventory(body, target))
            {
                targetOwner = body;
                break;
            }
        }
        if (targetOwner is not { } owner)
        {
            error = Loc.GetString("sw-cultist-test-invalid-target");
            return false;
        }

        var template = Spawn("SWCultistSelectionTemplate");
        try
        {
            if (!TryComp<AntagSelectionComponent>(template, out var selection) ||
                !_antags.TryGetNextAvailableDefinition((template, selection), out var definition, 20))
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
            var text = itemObjective
                ? Loc.GetString("sw-cultist-objective-sword", ("owner", Name(owner)), ("item", Name(target)))
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
                    plan.Uncertain = true;
                    var actualRole = HasManualCultistRole(candidate.Mind);
                    if (actualRole == true)
                        rule.Members.Add((candidate.Mind, candidate.Body));
                    else if (actualRole == null)
                        plan.Unresolved.Add((candidate.Mind, candidate.Body));
                    assignedAll = false;
                    break;
                }
                if (!accepted)
                {
                    assignedAll = false;
                    break;
                }
                var hasRole = HasManualCultistRole(candidate.Mind);
                if (hasRole != true)
                {
                    // TryMakeAntag's boolean does not certify that its void MakeAntag completed.
                    plan.Uncertain = true;
                    if (hasRole == null)
                        plan.Unresolved.Add((candidate.Mind, candidate.Body));
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

    private bool? HasManualCultistRole(EntityUid mindId)
    {
        try
        {
            if (!TryComp<MindComponent>(mindId, out var mind))
                return null;
            foreach (var role in mind.MindRoleContainer.ContainedEntities)
            {
                if (!TerminatingOrDeleted(role) && TryComp<MetaDataComponent>(role, out var meta) &&
                    meta.EntityPrototype?.ID == "MindRoleSWCultist")
                    return true;
            }
            return false;
        }
        catch (Exception e)
        {
            Log.Error($"Could not inspect manual cultist role for {mindId}: {e}");
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
            var hasRole = HasManualCultistRole(candidate.Mind);
            if (hasRole == null)
                continue;
            if (hasRole == true && !rule.Members.Any(member => member.Mind == candidate.Mind))
                rule.Members.Add(candidate);
            plan.Unresolved.RemoveAt(i);
        }
        if (plan.Unresolved.Count != 0 || rule.Members.Count == 0)
            return;
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
            plan.MainObjective ??= Spawn("SWCultistMainObjective");
            var main = plan.MainObjective.Value;
            Comp<SWCultistObjectiveComponent>(main).Rule = uid;
            _metadata.SetEntityName(main, plan.Text);
            _metadata.SetEntityDescription(main, plan.Text);
            plan.SurvivalObjective ??= Spawn("SWCultistSurvivalObjective");
            var survival = plan.SurvivalObjective.Value;
            var survivalComp = Comp<SWCultistObjectiveComponent>(survival);
            survivalComp.Rule = uid;
            survivalComp.Survival = true;
            _metadata.SetEntityName(survival, Loc.GetString("sw-cultist-objective-survive"));

            foreach (var member in rule.Members)
            {
                if (!TryComp<MindComponent>(member.Mind, out var mind) || HasManualCultistRole(member.Mind) != true)
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
                    var partners = rule.Members.Where(x => x.Mind != member.Mind).Select(x => Name(x.Body)).ToArray();
                    var partner = partners.Length > 0
                        ? Loc.GetString("sw-cultist-partner", ("partner", string.Join(", ", partners)))
                        : Loc.GetString("sw-cultist-alone");
                    _antags.SendBriefing(member.Body, Loc.GetString("sw-cultist-briefing", ("partner", partner), ("objective", plan.Text)), null, new SoundPathSpecifier("/Audio/Ambience/Antag/traitor_start.ogg"));
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
        var people = new List<(EntityUid Body, EntityUid Mind, string Job)>();
        foreach (var session in _players.Sessions)
        {
            if (TryPlayer(session, out var body, out var mind, out var job) && TargetJobs.Contains(job) &&
                !rule.Members.Any(m => m.Body == body))
                people.Add((body, mind, job));
        }
        var swords = new List<(EntityUid Item, EntityUid Owner)>();
        foreach (var (body, _, job) in people)
        {
            var proto = job == "SWJedi" ? "SWEnergySwordJedi" : job == "SWSith" ? "SWEnergySwordSith" : null;
            if (proto == null)
                continue;
            var query = EntityQueryEnumerator<MetaDataComponent>();
            while (query.MoveNext(out var item, out var meta))
            {
                if (meta.EntityPrototype?.ID == proto && InPersonalInventory(body, item))
                    swords.Add((item, body));
            }
        }
        var options = new List<bool>();
        if (people.Count > 0) options.Add(false);
        if (swords.Count > 0) options.Add(true);
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
                var target = RobustRandom.Pick(swords);
                rule.TargetItem = target.Item;
                text = Loc.GetString("sw-cultist-objective-sword", ("owner", Name(target.Owner)), ("item", Name(target.Item)));
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
        var main = Spawn("SWCultistMainObjective");
        var mainComp = Comp<SWCultistObjectiveComponent>(main);
        mainComp.Rule = uid;
        _metadata.SetEntityName(main, text);
        _metadata.SetEntityDescription(main, text);
        var survival = Spawn("SWCultistSurvivalObjective");
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
            var partners = rule.Members.Where(x => x.Mind != member.Mind).Select(x => Name(x.Body)).ToArray();
            var partnerText = partners.Length > 0 ? Loc.GetString("sw-cultist-partner", ("partner", string.Join(", ", partners))) : Loc.GetString("sw-cultist-alone");
            _antags.SendBriefing(member.Body, Loc.GetString("sw-cultist-briefing", ("partner", partnerText), ("objective", text)), null, new SoundPathSpecifier("/Audio/Ambience/Antag/traitor_start.ogg"));
        }
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
    private void OnRoundRestart(RoundRestartCleanupEvent args) => CleanupAllPending();

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
        return true;
    }

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
                rule.Attempts.Remove(mind);
            return;
        }
        var inOurSlot = rule.AttemptSlots.TryGetValue(mind, out var slot) && slot.Contains(sword);
        var uncontainedNullspace = !_containers.TryGetContainingContainer(sword, out _) && Transform(sword).MapUid == null;
        if (inOurSlot || uncontainedNullspace)
        {
            // This entity was created only for this attempt and has not entered the player's inventory.
            Del(sword);
            FinishAttempt(rule, mind);
            return;
        }
        // Do not destroy or duplicate an entity moved by an unexpected callback.
        rule.Cancelled.Add(mind);
        Log.Error($"Cultist sword attempt {sword} moved outside its controlled slot; preserving it and stopping retries");
    }

    private bool HasDeliverySpace(EntityUid body)
    {
        if ((_inventory.HasSlot(body, "pocket1") && !_inventory.TryGetSlotEntity(body, "pocket1", out _)) ||
            (_inventory.HasSlot(body, "pocket2") && !_inventory.TryGetSlotEntity(body, "pocket2", out _)))
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
            var sword = Spawn("SWEnergySwordYellow");
            rule.Attempts.Add(mind, sword);
            if (!_containers.Insert(sword, slot))
                return;
            if (_inventory.TryEquip(body, sword, "pocket1", silent: true) ||
                _inventory.TryEquip(body, sword, "pocket2", silent: true))
            {
                rule.Delivered.Add(mind);
                return;
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

    protected override void AppendRoundEndText(EntityUid uid, SWCultistGameRuleComponent component, GameRuleComponent gameRule, ref RoundEndTextAppendEvent args)
    {
        if (component.Members.Count == 0) return;
        var success = MainSuccess(component) && Survival(component);
        args.AddLine(Loc.GetString(success ? "sw-cultist-roundend-success" : "sw-cultist-roundend-failure"));
    }
}
