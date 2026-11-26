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

}
