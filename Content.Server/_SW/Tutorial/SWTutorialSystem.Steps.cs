using System.Linq;
using Content.Server._SW.Droids;
using Content.Server._SW.Targeting;
using Content.Server.Popups;
using Content.Server.Radio.EntitySystems;
using Content.Server._SW.Lathe.SWLathe;
using Content.Shared._SW.Tutorial;
using Content.Shared.Storage.Components;
using Content.Shared.Chat;
using Content.Shared.CombatMode;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Projectiles;
using Content.Shared.Rejuvenate;
using Content.Shared.Interaction;
using Content.Shared.Inventory.VirtualItem;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Robust.Shared.Containers;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._SW.Tutorial;

/// <summary>
/// The scenario: runs the steps from <see cref="SWTutorialStepPrototype"/> one after another.
/// </summary>
public sealed partial class SWTutorialSystem
{
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly MobThresholdSystem _mobThreshold = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly SharedCombatModeSystem _combat = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly SharedWieldableSystem _wieldable = default!;
    [Dependency] private readonly SWAutoTargetingGunSystem _autoGun = default!;
    [Dependency] private readonly SWDroidCommandSystem _droidCommand = default!;

    private const string RifleTag = "rifle";
    private const string MagazineSlot = "gun_magazine";

    /// <summary>
    /// How far short of the critical threshold the protected steps stop the damage.
    /// </summary>
    private const float ProtectionMargin = 5f;

    private ISawmill _funnel = default!;
    private bool _applyingScaledDamage;
    private int _runCounter;

    private void InitializeSteps()
    {
        _funnel = _logManager.GetSawmill("sw.tutorial.funnel");

        // Before the headset, which clears the channel once it has sent the message.
        SubscribeLocalEvent<SWTutorialPlayerComponent, EntitySpokeEvent>(OnStudentSpoke, before: [typeof(HeadsetSystem)]);
        SubscribeLocalEvent<SWTutorialPlayerComponent, BeforeDamageChangedEvent>(OnStudentDamage);
        SubscribeLocalEvent<SWTutorialAllyComponent, BeforeDamageChangedEvent>(OnAllyDamage);
        SubscribeLocalEvent<SWTutorialEnemyComponent, BeforeDamageChangedEvent>(OnEnemyDamage);
        SubscribeNetworkEvent<SWTutorialReturnEvent>(OnReturnPressed);
        SubscribeNetworkEvent<SWTutorialHelpEvent>(OnHelp);
        InitializePractice();

        CommandBinds.Builder
            .BindBefore(EngineKeyFunctions.Use, new PointerInputCmdHandler(OnStudentClick), typeof(SharedInteractionSystem))
            .Register<SWTutorialSystem>();
    }

    private void ShutdownSteps()
    {
        CommandBinds.Unregister<SWTutorialSystem>();
    }

    private void UpdateSteps()
    {
        foreach (var (user, student) in _students.ToArray())
        {
            if (student.DisconnectedAt != null || !_player.TryGetSessionById(user, out var session))
                continue;

            if (TerminatingOrDeleted(student.Map) || !InPractice(student, student.Body))
            {
                FunnelLeft(student, "scene unavailable");
                Leave(session, student);
                _chat.DispatchServerMessage(session, Loc.GetString("sw-tutorial-technical-error"));
                continue;
            }
            var step = CurrentStep(student);
            if (!student.Scenario.GuidedPractice)
                ReloadIfEmpty(student);
            UpdateMissingItem(session, student, step);
            UpdateBeat(session, student, step);

            if (!student.Blocked && IsDone(session, student, step.Completion))
                FinishStep(session, student);
        }
    }

    private SWTutorialStepPrototype CurrentStep(Student student)
    {
        return _proto.Index(student.Scenario.Steps[student.StepIndex]);
    }

    private void SendState(ICommonSession session, Student student, SWTutorialStepPrototype? step = null)
    {
        step ??= CurrentStep(student);
        var prompt = student.MissingItem && step.RequiresHeld != null ? step.RequiresHeld.Prompt : student.Prompt ?? step.Prompt;
        var (focusEntity, focusCoordinates) = GetFocus(student, step);
        RaiseNetworkEvent(new SWTutorialStateEvent(true, step.ID, prompt, student.Highlight, student.Note, student.Attempt, student.Revision, student.Unlocked.ToArray(), student.Beat, focusEntity, focusCoordinates), session);
    }

    private (NetEntity?, NetCoordinates?) GetFocus(Student student, SWTutorialStepPrototype step)
    {
        if (student.MissingItem || student.Beat < 0 || student.Beat >= step.Beats.Count
            || step.Beats[student.Beat].Focus is not { } focus)
            return (null, null);

        if (focus.Tag != null)
        {
            foreach (var entity in GetTagged(student, focus.Tag))
            {
                if (InPractice(student, entity))
                    return (GetNetEntity(entity), null);
            }
        }

        if (focus.Point != null && student.Points.TryGetValue(focus.Point, out var points) && points.Count > 0)
            return (null, GetNetCoordinates(points[0]));

        return (null, null);
    }

    /// <summary>
    /// The last beat whose conditions hold. Its line and highlight replace the step's own.
    /// </summary>
    private int PickBeat(ICommonSession session, Student student, SWTutorialStepPrototype step)
    {
        for (var i = step.Beats.Count - 1; i >= 0; i--)
        {
            if (step.Beats[i].When.All(condition => IsDone(session, student, condition)))
                return i;
        }

        return -1;
    }

    private void UpdateBeat(ICommonSession session, Student student, SWTutorialStepPrototype step)
    {
        var beat = PickBeat(session, student, step);
        if (beat == student.Beat || beat < 0)
            return;

        student.Beat = beat;
        student.Prompt = step.Beats[beat].Hint;
        student.Highlight = step.Beats[beat].Highlight;
        student.Note = null;
        SendState(session, student, step);
    }

    private void StartStep(ICommonSession session, Student student, int index)
    {
        student.StepIndex = index;
        student.Revision++;
        student.Blocked = false;
        student.StepStartedAt = _timing.CurTime;
        student.StepStartPosition = Transform(student.Body).Coordinates;
        student.MistakesMade = 0;
        student.LastChannel = null;
        student.ReturnPressed = false;
        student.Note = null;
        student.MissingItem = false;

        var step = CurrentStep(student);
        student.Unlocked.UnionWith(step.Show);
        if (step.Checkpoint)
            SaveCheckpoint(session, student.Scenario, index);
        student.Prompt = step.Prompt;
        student.Highlight = step.Highlight;
        student.Beat = -1;

        RunActions(student, step.OnStart);
        student.Beat = PickBeat(session, student, step);
        if (student.Beat >= 0)
        {
            student.Prompt = step.Beats[student.Beat].Hint;
            student.Highlight = step.Beats[student.Beat].Highlight;
        }
        SendState(session, student, step);
    }

    private void FinishStep(ICommonSession session, Student student)
    {
        var step = CurrentStep(student);
        var fight = step.Completion is SWTutorialFightOverCondition over
            ? $" enemiesleft={GetTagged(student, over.Tag).Count(e => !AllGone(e))} standing={_mobState.IsAlive(student.Body)}"
            : string.Empty;
        Funnel(student,
            $"step {step.ID} secs={(_timing.CurTime - student.StepStartedAt).TotalSeconds:F1} mistakes={student.MistakesMade}{fight}");

        foreach (var entity in student.UntilStepEnd)
        {
            QueueDel(entity);
        }
        student.UntilStepEnd.Clear();

        if (student.StepIndex + 1 < student.Scenario.Steps.Count)
        {
            StartStep(session, student, student.StepIndex + 1);
            return;
        }

        Graduate(session, student);
    }

    /// <summary>
    /// The droids keep firing at the body until they are gone and those bolts fly past it, so the student would come
    /// back into them. The map belongs to this student, so nothing else can be flying on it.
    /// </summary>
    private void DeleteBoltsInFlight(Student student)
    {
        var map = Transform(student.Body).MapUid;
        var bolts = new List<EntityUid>();
        var query = EntityQueryEnumerator<ProjectileComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapUid == map)
                bolts.Add(uid);
        }

        foreach (var bolt in bolts)
        {
            Del(bolt);
        }
    }

    private void UpdateMissingItem(ICommonSession session, Student student, SWTutorialStepPrototype step)
    {
        var missing = step.RequiresHeld is { } required && !IsHolding(student, required.Tag, required.Proto);
        if (missing == student.MissingItem)
            return;

        student.MissingItem = missing;
        if (missing)
            student.MistakesMade++;

        SendState(session, student, step);
    }

    private bool IsHolding(Student student, string? tag, EntProtoId? proto)
    {
        return _hands.EnumerateHeld(student.Body).Any(item =>
            tag != null && GetTagged(student, tag).Contains(item)
            || proto != null && MetaData(item).EntityPrototype?.ID == proto);
    }

    private void ReportMistake(ICommonSession session, Student student, SWTutorialMistake mistake, string? tag = null)
    {
        var hint = CurrentStep(student).Mistakes.FirstOrDefault(m => m.Mistake == mistake && (m.Tag == null || m.Tag == tag));
        if (hint == null)
            return;

        student.MistakesMade++;
        student.Note = hint.Prompt;
        SendState(session, student);
    }

    private bool IsDone(ICommonSession session, Student student, SWTutorialCondition condition)
    {
        switch (condition)
        {
            case SWTutorialReachCondition reach:
                var position = Transform(student.Body).Coordinates;
                return student.Points[reach.Point].Any(p => _transform.InRange(position, p, reach.Range));

            case SWTutorialHoldingCondition holding:
                return IsHolding(student, holding.Tag, holding.Proto);

            case SWTutorialContainedCondition contained:
                return IsContained(student, contained);

            case SWTutorialLoadedCondition loaded:
                return IsPracticeLoaded(student, loaded);

            case SWTutorialDefeatedCondition defeated:
                return IsPracticeDefeated(session, student, defeated);

            case SWTutorialPatientHealedCondition patient:
                return IsPatientHealed(student, patient);

            case SWTutorialPoweredCondition powered:
                return IsPracticePowered(student, powered);

            case SWTutorialProducedCondition:
                return GetTagged(student, "product").Any(item => InPractice(student, item));

            case SWTutorialPlacedCondition placed:
                return IsPlaced(student, placed);

            case SWTutorialNotCondition not:
                return !IsDone(session, student, not.Condition);

            case SWTutorialActiveHoldingCondition active:
                return _hands.GetActiveItem(student.Body) is { } activeItem && GetTagged(student, active.Tag).Contains(activeItem);

            case SWTutorialBoxOpenCondition box:
                return GetTagged(student, box.Tag).Any(item => TryComp<EntityStorageComponent>(item, out var storage) && storage.Open);

            case SWTutorialUiOpenCondition ui:
                return TryComp<UserInterfaceUserComponent>(student.Body, out var user)
                       && GetTagged(student, ui.Tag).Any(user.OpenInterfaces.ContainsKey);

            case SWTutorialLatheRunningCondition running:
                return GetTagged(student, running.Tag).Any(item => TryComp<SWLatheComponent>(item, out var lathe) && !lathe.ProductionDisabled);

            case SWTutorialWieldedCondition wielded:
                return IsWielded(student, wielded.Tag);

            case SWTutorialCombatModeCondition:
                return _combat.IsInCombatMode(student.Body);

            case SWTutorialGoneCondition gone:
                return IsGone(session, student, gone);

            case SWTutorialHealedCondition healed:
                return TotalDamage(student.Body) < student.DamageMark;

            case SWTutorialSaidOnRadioCondition radio:
                return student.LastChannel == radio.Channel.Id;

            case SWTutorialFightOverCondition fight:
                return GetTagged(student, fight.Tag).Count > 0 && AllGone(student, fight.Tag) && _mobState.IsAlive(student.Body);

            case SWTutorialReturnPressedCondition:
                return student.ReturnPressed;

            default:
                Log.Error($"Unknown tutorial condition {condition.GetType().Name}");
                student.Blocked = true;
                student.Note = "sw-tutorial-technical-error";
                student.Unlocked.Add(SWTutorialHudElement.All);
                SendState(session, student);
                return false;
        }
    }

    private bool IsPlaced(Student student, SWTutorialPlacedCondition placed)
    {
        var items = GetTagged(student, placed.Tag);
        if (items.Count == 0 || IsHolding(student, placed.Tag, null))
            return false;

        var places = new List<EntityCoordinates>();
        if (placed.Point != null && student.Points.TryGetValue(placed.Point, out var points))
            places.AddRange(points);
        if (placed.NearTag != null)
            places.AddRange(GetTagged(student, placed.NearTag).Where(e => InPractice(student, e)).Select(e => Transform(e).Coordinates));

        return items.All(item => !InPractice(student, item)
                                 || places.Any(p => _transform.InRange(Transform(item).Coordinates, p, placed.Range)));
    }

    private bool IsGone(ICommonSession session, Student student, SWTutorialGoneCondition gone)
    {
        if (!AllGone(student, gone.Tag))
            return false;

        if (gone.WieldedTag == null || IsWielded(student, gone.WieldedTag))
            return true;

        // The lucky shot from one hand teaches the wrong thing, so it does not count.
        ReportMistake(session, student, SWTutorialMistake.OneHanded);
        student.Tagged.Remove(gone.Tag);
        RunActions(student, gone.Retry);
        return false;
    }

    private bool IsWielded(Student student, string tag)
    {
        return GetTagged(student, tag).Any(item => TryComp<WieldableComponent>(item, out var wieldable) && wieldable.Wielded);
    }

    private bool AllGone(Student student, string tag)
    {
        return GetTagged(student, tag).All(AllGone);
    }

    private bool AllGone(EntityUid entity)
    {
        return TerminatingOrDeleted(entity)
               || TryComp<MobStateComponent>(entity, out var state) && state.CurrentState == MobState.Dead;
    }

    private IReadOnlyList<EntityUid> GetTagged(Student student, string tag)
    {
        return student.Tagged.TryGetValue(tag, out var list) ? list : Array.Empty<EntityUid>();
    }

    private float TotalDamage(EntityUid body)
    {
        return TryComp<DamageableComponent>(body, out var damageable) ? damageable.TotalDamage.Float() : 0f;
    }

    private void RunActions(Student student, List<SWTutorialAction> actions)
    {
        foreach (var action in actions)
        {
            RunAction(student, action);
        }
    }

    private void RunAction(Student student, SWTutorialAction action)
    {
        switch (action)
        {
            case SWTutorialDamagePatientAction patient:
                DamagePracticePatient(student, patient);
                break;

            case SWTutorialSpawnAction spawn:
                foreach (var coordinates in student.Points[spawn.Point])
                {
                    var entity = Spawn(spawn.Proto, coordinates.Offset(spawn.Offset));
                    if (spawn.Tag != null)
                    {
                        if (!student.Tagged.TryGetValue(spawn.Tag, out var list))
                            student.Tagged[spawn.Tag] = list = new List<EntityUid>();

                        list.Add(entity);
                    }

                    if (spawn.UntilStepEnd)
                        student.UntilStepEnd.Add(entity);
                }
                break;

            case SWTutorialMoveAction move:
                foreach (var entity in GetTagged(student, move.Tag))
                {
                    if (!TerminatingOrDeleted(entity))
                        _transform.SetCoordinates(entity, student.Points[move.Point][0]);
                }
                break;

            case SWTutorialDeleteAction delete:
                // At once and not queued: what is queued can still fire this tick, after the bolts are cleared.
                foreach (var entity in GetTagged(student, delete.Tag))
                {
                    if (!TerminatingOrDeleted(entity))
                        Del(entity);
                }
                student.Tagged.Remove(delete.Tag);
                break;

            case SWTutorialDamageStudentAction damage:
                _damageable.ChangeDamage(student.Body, damage.Damage, true);
                student.DamageMark = TotalDamage(student.Body);
                if (damage.Popup != null)
                    _popup.PopupEntity(Loc.GetString(damage.Popup.Value), student.Body, student.Body, PopupType.LargeCaution);
                break;

            case SWTutorialFreeHandAction:
                FreeHand(student.Body);
                break;

            case SWTutorialRearmAction rearm:
                Rearm(student, rearm.Tag);
                break;

            case SWTutorialKillStudentAction kill:
                _mobState.ChangeMobState(student.Body, MobState.Dead);
                if (kill.Popup != null)
                    _popup.PopupEntity(Loc.GetString(kill.Popup.Value), student.Body, student.Body, PopupType.LargeCaution);
                break;

            case SWTutorialReviveStudentAction revive:
                RaiseLocalEvent(student.Body, new RejuvenateEvent());
                _transform.SetCoordinates(student.Body, student.Points[revive.Point][0]);
                DeleteBoltsInFlight(student);
                break;

            case SWTutorialSendDroidsAction droids:
                foreach (var droid in GetTagged(student, droids.Tag))
                {
                    _droidCommand.TrySetFollowTarget(droid, student.Body);
                    _autoGun.SetForcedTarget(droid, student.Body);
                }
                break;

            default:
                Log.Error($"Unknown tutorial action {action.GetType().Name}");
                break;
        }
    }

    /// <summary>
    /// The tutorial does not teach reloading, and an empty rifle would leave the student stuck on a step.
    /// </summary>
    private void ReloadIfEmpty(Student student)
    {
        foreach (var gun in GetTagged(student, RifleTag))
        {
            if (TerminatingOrDeleted(gun))
                continue;

            var ammo = new GetAmmoCountEvent();
            RaiseLocalEvent(gun, ref ammo);
            if (ammo.Count > 0
                || !_containers.TryGetContainer(gun, MagazineSlot, out var slot)
                || slot.ContainedEntities.FirstOrDefault() is not { Valid: true } magazine
                || MetaData(magazine).EntityPrototype is not { } magazineProto)
            {
                continue;
            }

            Del(magazine);
            var fresh = Spawn(magazineProto.ID, Transform(gun).Coordinates);
            if (!_containers.Insert(fresh, slot))
                Del(fresh);
        }
    }

    private void Rearm(Student student, string tag)
    {
        if (GetTagged(student, tag).FirstOrDefault(e => !TerminatingOrDeleted(e)) is not { Valid: true } gun)
            return;

        foreach (var item in _hands.EnumerateHeld(student.Body).ToArray())
        {
            if (item != gun && !HasComp<VirtualItemComponent>(item))
                QueueDel(item);
        }

        if (_hands.IsHolding(student.Body, gun, out var hand))
            _hands.SetActiveHand(student.Body, hand);
        else if (!_hands.TryPickupAnyHand(student.Body, gun, checkActionBlocker: false))
            return;

        if (TryComp<WieldableComponent>(gun, out var wieldable) && !wieldable.Wielded)
            _wieldable.TryWield(gun, wieldable, student.Body);
    }

    private void FreeHand(EntityUid body)
    {
        foreach (var item in _hands.EnumerateHeld(body).ToArray())
        {
            if (TryComp<WieldableComponent>(item, out var wieldable) && wieldable.Wielded)
                _wieldable.TryUnwield(item, wieldable, body, force: true);
        }

        // The placeholder item of the wielded gun is only deleted at the end of the tick, so it counts as empty here.
        foreach (var hand in _hands.EnumerateHands(body))
        {
            if (!_hands.TryGetHeldItem(body, hand, out var held) || HasComp<VirtualItemComponent>(held))
            {
                _hands.SetActiveHand(body, hand);
                return;
            }
        }
    }

    private IEnumerable<string> RequiredPoints(SWTutorialScenarioPrototype scenario)
    {
        yield return SpawnPointName;
        foreach (var id in scenario.Steps)
        {
            var step = _proto.Index(id);
            if (step.Completion is SWTutorialReachCondition reach)
                yield return reach.Point;

            var actions = scenario.Setup.Concat(step.OnStart);
            if (step.Completion is SWTutorialGoneCondition gone)
                actions = actions.Concat(gone.Retry);
            if (step.Completion is SWTutorialDefeatedCondition defeated)
                actions = actions.Concat(defeated.Retry);

            foreach (var action in actions)
            {
                switch (action)
                {
                    case SWTutorialSpawnAction spawn:
                        yield return spawn.Point;
                        break;
                    case SWTutorialMoveAction move:
                        yield return move.Point;
                        break;
                    case SWTutorialReviveStudentAction revive:
                        yield return revive.Point;
                        break;
                }
            }
        }
    }

    private void OnStudentSpoke(Entity<SWTutorialPlayerComponent> ent, ref EntitySpokeEvent args)
    {
        if (!_player.TryGetSessionByEntity(ent, out var session)
            || !_students.TryGetValue(session.UserId, out var student))
        {
            return;
        }

        if (CurrentStep(student).Completion is SWTutorialSaidOnRadioCondition radio && args.Channel?.ID != radio.Channel.Id)
            ReportMistake(session, student, SWTutorialMistake.NotOnRadio);
    }

    private void OnStudentDamage(Entity<SWTutorialPlayerComponent> ent, ref BeforeDamageChangedEvent args)
    {
        if (_applyingScaledDamage
            || !_player.TryGetSessionByEntity(ent, out var session)
            || !_students.TryGetValue(session.UserId, out var student))
        {
            return;
        }

        var step = CurrentStep(student);
        if (step.Protected
            && _mobThreshold.TryGetThresholdForState(ent, MobState.Critical, out var critical)
            && TotalDamage(ent) + args.Damage.GetTotal().Float() >= critical.Value.Float() - ProtectionMargin)
        {
            args.Cancelled = true;
            return;
        }

        // Blaster bolts ignore resistances, so the share is taken here and not in DamageModifyEvent.
        if (step.DamageTaken >= 1f || !args.Damage.AnyPositive())
            return;

        args.Cancelled = true;
        _applyingScaledDamage = true;
        // A bolt outlives the droid that fired it.
        var origin = TerminatingOrDeleted(args.Origin) ? null : args.Origin;
        try
        {
            _damageable.ChangeDamage(ent.Owner, args.Damage * step.DamageTaken, true, origin: origin);
        }
        finally
        {
            _applyingScaledDamage = false;
        }
    }

    /// <summary>
    /// Friendly fire exists in the real game, so the shot lands and the student hears about it, without punishment.
    /// </summary>
    private void OnAllyDamage(Entity<SWTutorialAllyComponent> ent, ref BeforeDamageChangedEvent args)
    {
        if (!args.Damage.AnyPositive())
            return;
        if (args.Origin is not { } origin
            || !HasComp<SWTutorialPlayerComponent>(origin)
            || !_player.TryGetSessionByEntity(origin, out var session)
            || !_students.TryGetValue(session.UserId, out var student))
        {
            return;
        }

        args.Cancelled = true;
        ReportMistake(session, student, SWTutorialMistake.HitAlly);
    }

    private void OnEnemyDamage(Entity<SWTutorialEnemyComponent> ent, ref BeforeDamageChangedEvent args)
    {
        if (HasComp<SWTutorialEnemyComponent>(args.Origin))
            args.Cancelled = true;
    }

    private void OnReturnPressed(SWTutorialReturnEvent ev, EntitySessionEventArgs args)
    {
        if (_students.TryGetValue(args.SenderSession.UserId, out var student))
            student.ReturnPressed = true;
    }

    /// <summary>
    /// The server sees every click of a student, including those that do nothing, and only watches here.
    /// </summary>
    private bool OnStudentClick(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
    {
        if (session == null
            || !uid.IsValid()
            || !_students.TryGetValue(session.UserId, out var student)
            || student.DisconnectedAt != null)
        {
            return false;
        }

        var tag = student.Tagged.FirstOrDefault(pair => pair.Value.Contains(uid)).Key;
        if (tag == null)
            return false;

        if (!student.Scenario.GuidedPractice && !_combat.IsInCombatMode(student.Body))
            ReportMistake(session, student, SWTutorialMistake.NoCombatMode, tag);

        if (!_interaction.InRangeUnobstructed(student.Body, uid))
            ReportMistake(session, student, SWTutorialMistake.TooFar, tag);
        else if (_hands.GetActiveItem(student.Body) != null)
            ReportMistake(session, student, SWTutorialMistake.HandBusy, tag);

        return false;
    }

    private void Funnel(Student student, string line)
    {
        // No player name or id: a counter is enough to follow one run through the log.
        _funnel.Info($"run={student.Run} scenario={student.Scenario.ID} version={student.Scenario.Version} {line}");
    }

    private void FunnelLeft(Student student, string reason)
    {
        Funnel(student,
            $"left reason={reason} step={CurrentStep(student).ID} stepsecs={(_timing.CurTime - student.StepStartedAt).TotalSeconds:F1} totalsecs={(_timing.CurTime - student.StartedAt - student.SuspendedTime).TotalSeconds:F1}");
    }
}
