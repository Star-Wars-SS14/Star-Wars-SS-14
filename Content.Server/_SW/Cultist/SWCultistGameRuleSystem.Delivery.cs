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
            CleanupAttempt(rule, mind);
        }
    }

}
