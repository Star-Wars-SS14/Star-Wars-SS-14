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

public sealed partial class SWCultistGameRuleSystem : GameRuleSystem<SWCultistGameRuleComponent>
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
        SubscribeLocalEvent<SWCultistTrackedBodyComponent, BeingGibbedEvent>(OnCultistGibbed);
        SubscribeLocalEvent<SWCultistTrackedBodyComponent, EntityTerminatingEvent>(OnCultistBodyTerminating);
        SubscribeLocalEvent<SWCultistBodyTrackerComponent, MindGotAddedEvent>(OnCultistMindAdded);
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

}
