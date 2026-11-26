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
            TrackCultistBody(mind, body);
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
                AddCultistMember(rule, mind, body);
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
                AddCultistMember(rule, candidate.Mind, candidate.Body);
            rule.PendingAutomaticRoles.RemoveAt(i);
        }
        if (rule.PendingAutomaticRoles.Count != 0)
            return;
        rule.AutomaticSelectionPending = false;
        if (rule.Members.Count != 0)
            AssignObjectives(uid, rule);
    }

    /// <summary>Administrative, explicit test setup. Never replaces an existing team or objective.</summary>
}
