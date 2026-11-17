using Content.Shared.Access.Systems;
using Content.Shared.Ghost;
using Content.Shared.Popups;
using Content.Shared.UserInterface;
using Robust.Shared.Network;

namespace Content.Shared._SW.TaskSystem;

[Virtual]
public class SWSharedTaskConsoleystem : EntitySystem
{
    [Dependency] private readonly AccessReaderSystem _accessReaderSystem = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly INetManager _net = default!;

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
        if (HasOrdinaryAccess(args.User, ent) || CanAdminGhostView(args.User, ent))
        {
            return;
        }

        args.Cancel();
        if (!args.Silent)
        {
            _popup.PopupClient(Loc.GetString("sw-task-console-access-denied"), ent, args.User);
        }
    }

    public bool HasOrdinaryAccess(EntityUid user, Entity<TaskConsoleComponent> console)
    {
        return _accessReaderSystem.IsAllowed(user, console);
    }

    // The client only predicts the interaction; the server decides from its own session state.
    private bool CanAdminGhostView(EntityUid user, Entity<TaskConsoleComponent> console)
    {
        if (!IsMainFactionConsole(console) || !TryComp<GhostComponent>(user, out var ghost) || !ghost.CanGhostInteract)
            return false;

        if (_net.IsClient)
            return true;

        var ev = new SWTaskConsoleAdminGhostAccessEvent(user);
        RaiseLocalEvent(console.Owner, ref ev);
        return ev.Allowed;
    }

    public static bool IsMainFactionConsole(Entity<TaskConsoleComponent> console)
    {
        return console.Comp.Faction is "EmpireFaction" or "RepublicFaction";
    }
}

[ByRefEvent]
public struct SWTaskConsoleAdminGhostAccessEvent(EntityUid user)
{
    public EntityUid User = user;
    public bool Allowed;
}
