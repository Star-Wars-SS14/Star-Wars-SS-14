using Content.Server.Access.Components;
using Content.Shared._SW.Droids;
using Content.Shared._SW.TaskSystem;
using Content.Shared.Inventory;
using Content.Shared.Roles;
using Content.Shared.Silicons.Borgs.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._SW.Droids;

public sealed class SWDroidLoyaltySystem : EntitySystem
{
    [Dependency] private readonly InventorySystem _inventory = default!;

    public bool IsTargetProtected(EntityUid droid, EntityUid target)
    {
        // A mode that knows real allegiance decides first; a card or job id can be taken from a body.
        var decision = new SWDroidTargetAllegianceEvent(target);
        RaiseLocalEvent(droid, ref decision);
        if (decision.Protected is { } decided)
            return decided;

        if (!TryComp<BorgChassisComponent>(droid, out var chassis) ||
            !TryGetTargetJob(target, out var targetJob))
        {
            return false;
        }

        foreach (var module in chassis.ModuleContainer.ContainedEntities)
        {
            if (!TryComp<SWDroidLoyaltyModuleComponent>(module, out var loyalty) ||
                !loyalty.ProtectedJobs.Contains(targetJob))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private bool TryGetTargetJob(EntityUid target, out ProtoId<JobPrototype> job)
    {
        job = default;

        if (_inventory.TryGetSlotEntity(target, "id", out var idCard) &&
            TryComp<PresetIdCardComponent>(idCard, out var idCardComp) &&
            idCardComp.JobName != null)
        {
            job = idCardComp.JobName.Value;
            return true;
        }

        if (TryComp<PresetIdCardComponent>(target, out var targetIdComp) &&
            targetIdComp.JobName != null)
        {
            job = targetIdComp.JobName.Value;
            return true;
        }

        if (TryComp<SWFactionMemberComponent>(target, out var factionMember) &&
            factionMember.Job != null)
        {
            job = new ProtoId<JobPrototype>(factionMember.Job);
            return true;
        }

        return false;
    }
}

/// <summary>Raised on a droid choosing a target. A handler sets <see cref="Protected"/> when it knows the answer.</summary>
[ByRefEvent]
public record struct SWDroidTargetAllegianceEvent(EntityUid Target, bool? Protected = null);
