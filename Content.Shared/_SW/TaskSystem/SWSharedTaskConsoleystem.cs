using Content.Shared.Access.Systems;
using Content.Shared.Popups;
using Content.Shared.UserInterface;
using Robust.Shared.Prototypes;

namespace Content.Shared._SW.TaskSystem;

[Virtual]
public class SWSharedTaskConsoleystem : EntitySystem
{
    [Dependency] private readonly AccessReaderSystem _accessReaderSystem = default!;
    [Dependency] private readonly IComponentFactory _componentFactory = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<TaskConsoleComponent, ActivatableUIOpenAttemptEvent>(OnOpen);
    }

    protected void OnOpen(Entity<TaskConsoleComponent> ent, ref ActivatableUIOpenAttemptEvent args)
    {
        if (args.Cancelled)
        {
            return;
        }
        if (_accessReaderSystem.IsAllowed(args.User, ent) && IsOfficer(args.User, ent))
        {
            return;
        }

        args.Cancel();
        if (!args.Silent)
        {
            _popup.PopupClient(Loc.GetString("sw-task-console-officers-only"), ent, args.User);
        }
    }

    /// <summary>
    /// Only officers of the console's faction see its task list.
    /// Ranks are read from the faction prototype because clients never get the faction entity.
    /// </summary>
    public bool IsOfficer(EntityUid user, Entity<TaskConsoleComponent> console)
    {
        if (!TryComp<SWFactionMemberComponent>(user, out var member) || member.Faction != console.Comp.Faction)
        {
            return false;
        }

        if (member.IsLeader)
        {
            return true;
        }

        if (member.Job == null)
        {
            return false;
        }

        if (console.Comp.LeaderJobs.Contains(member.Job))
        {
            return true;
        }

        return _prototypeManager.TryIndex<EntityPrototype>(console.Comp.Faction, out var factionProto)
            && factionProto.TryGetComponent<SWFactionComponent>(out var faction, _componentFactory)
            && faction.CommandRanks.TryGetValue(member.Job, out var rank)
            && rank >= console.Comp.MinOfficerRank;
    }
}
