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

public sealed partial class SWCultistGameRuleSystem
{
    private void AddCultistMember(SWCultistGameRuleComponent rule, EntityUid mind, EntityUid body)
    {
        TrackCultistBody(mind, body);
        rule.Members.Add((mind, body));
    }

    private void TrackCultistBody(EntityUid mind, EntityUid body)
    {
        if (TerminatingOrDeleted(mind) || TerminatingOrDeleted(body))
            return;
        var tracker = EnsureComp<SWCultistBodyTrackerComponent>(mind);
        // If the mind has already moved, follow its current physical body.
        if (TryComp<MindComponent>(mind, out var mindComp) && mindComp.OwnedEntity is { } current &&
            !HasComp<GhostComponent>(current) && HasComp<MobStateComponent>(current))
            body = current;
        if (tracker.Body != body)
        {
            tracker.Body = body;
            tracker.ConfirmedDeath = false;
        }
        EnsureComp<SWCultistTrackedBodyComponent>(body).Mind = mind;
    }

    private void OnCultistMindAdded(Entity<SWCultistBodyTrackerComponent> ent, ref MindGotAddedEvent args)
    {
        var body = args.Container.Owner;
        // Minds also move to brains during surgery and gibbing; only mobs replace the body.
        if (!HasComp<GhostComponent>(body) && HasComp<MobStateComponent>(body))
            TrackCultistBody(ent.Owner, body);
    }

    private void OnCultistGibbed(Entity<SWCultistTrackedBodyComponent> ent, ref BeingGibbedEvent args)
    {
        if (TryComp<SWCultistBodyTrackerComponent>(ent.Comp.Mind, out var tracker) && tracker.Body == ent.Owner)
            tracker.ConfirmedDeath = true;
    }

    private void OnCultistBodyTerminating(Entity<SWCultistTrackedBodyComponent> ent, ref EntityTerminatingEvent args)
    {
        if (TryComp<SWCultistBodyTrackerComponent>(ent.Comp.Mind, out var tracker) && tracker.Body == ent.Owner &&
            TryComp<MobStateComponent>(ent.Owner, out var state) && state.CurrentState == MobState.Dead)
            tracker.ConfirmedDeath = true;
    }

    public bool HasCultistRule()
    {
        var rules = AllEntityQuery<SWCultistGameRuleComponent>();
        while (rules.MoveNext(out var uid, out _))
        {
            if (HasComp<GameRuleComponent>(uid))
                return true;
        }
        return false;
    }

    // Called only by the sealed task during the actual post-round summary. Unknown is failure.
    public bool AllAssignedCultistsDead()
    {
        if (GameTicker.RunLevel != GameRunLevel.PostRound)
            return false;
        var rules = AllEntityQuery<SWCultistGameRuleComponent>();
        while (rules.MoveNext(out var uid, out var rule))
        {
            if (!HasComp<GameRuleComponent>(uid))
                continue;
            foreach (var pending in rule.PendingAutomaticRoles.Concat(rule.ManualPlan?.Unresolved ?? new()))
            {
                var role = HasCultistRole(rule, pending.Mind);
                if (role == null || (role == true && !IsCultistDead(pending.Mind, pending.Body)))
                    return false;
            }
            foreach (var member in rule.Members)
            {
                if (!IsCultistDead(member.Mind, member.Body))
                    return false;
            }
        }
        return true;
    }

    private bool IsCultistDead(EntityUid mind, EntityUid originalBody)
    {
        var body = originalBody;
        var gibbed = false;
        if (TryComp<SWCultistBodyTrackerComponent>(mind, out var tracker))
        {
            body = tracker.Body;
            gibbed = tracker.ConfirmedDeath;
        }
        if (TryComp<MindComponent>(mind, out var mindComp) && mindComp.OwnedEntity is { } current &&
            !HasComp<GhostComponent>(current) && HasComp<MobStateComponent>(current))
        {
            if (current != body)
                gibbed = false;
            body = current;
        }
        if (!TerminatingOrDeleted(body))
            return TryComp<MobStateComponent>(body, out var state) && state.CurrentState == MobState.Dead;
        return gibbed;
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

}
