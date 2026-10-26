using Content.Server.NPC;
using Content.Server.NPC.HTN.Preconditions;
using Content.Shared._SW.Droids;
using Content.Shared.Silicons.Borgs.Components;

namespace Content.Server._SW.Droids;

public sealed partial class SWDroidHasAICorePrecondition : HTNPrecondition
{
    [Dependency] private readonly IEntityManager _entManager = default!;

    public override bool IsMet(NPCBlackboard blackboard)
    {
        if (!blackboard.TryGetValue<EntityUid>(NPCBlackboard.Owner, out var owner, _entManager) ||
            !_entManager.TryGetComponent<BorgChassisComponent>(owner, out var chassis))
        {
            return false;
        }

        foreach (var module in chassis.ModuleContainer.ContainedEntities)
        {
            if (_entManager.HasComponent<SWDroidAICoreModuleComponent>(module))
                return true;
        }

        return false;
    }
}
