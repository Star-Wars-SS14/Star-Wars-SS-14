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
