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
                TrackCultistBody(candidate.Mind, candidate.Body);
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
                        AddCultistMember(rule, candidate.Mind, candidate.Body);
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
                AddCultistMember(rule, candidate.Mind, candidate.Body);
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
                AddCultistMember(rule, candidate.Mind, candidate.Body);
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

}
