using System.Linq;
using Content.Shared._SW._Senate.Director;
using Content.Shared._SW._Senate.Empire;
using Content.Shared._SW.TaskSystem;
using Content.Shared.Ghost;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Player;

namespace Content.Server._SW._Senate.Consoles;

/// <summary>Strategic information belongs to the character's side, never to an inserted ID card.</summary>
public sealed class SWStrategicVisibilitySystem : EntitySystem
{
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<UserInterfaceComponent, ActivatableUIOpenAttemptEvent>(OnOpen);
        SubscribeLocalEvent<BoundUserInterfaceMessageAttempt>(OnMessage);
        // Covers direct/server-opened UIs as well as normal activatable consoles.
        SubscribeLocalEvent<UserInterfaceComponent, BoundUIOpenedEvent>(OnOpened);
        UpdatesBefore.Add(typeof(SWStatisticsConsoleSystem));
        UpdatesBefore.Add(typeof(SWMinistryWorkConsoleSystem));
        UpdatesBefore.Add(typeof(SWSenateWorkConsoleSystem));
    }

    public string GetFaction(ICommonSession session) => GetFaction(session.AttachedEntity);

    public string GetFaction(EntityUid? user)
    {
        if (user is not { } uid || Deleted(uid) || HasComp<GhostComponent>(uid))
            return "none";
        if (HasComp<SWDarkCouncilMemberComponent>(uid))
            return TryComp<SWFactionMemberComponent>(uid, out var councilFaction) && councilFaction.Faction == "EmpireFaction" ? "empire" : "none";
        if (TryComp<SWFactionMemberComponent>(uid, out var member))
            return member.Faction switch
            {
                "EmpireFaction" => "empire",
                "RepublicFaction" => "republic",
                _ => "none",
            };
        // Existing Senate civilians and officials predate the combat faction component.
        return "republic";
    }

    public bool CanRead(EntityUid console, EntityUid actor) =>
        HasComp<SWEmpireCouncilConsoleComponent>(console) ? CouncilSphere(actor) != SWEmpireCouncilSphere.None
            : ConsoleFaction(console) is not { } faction || GetFaction(actor) == faction;

    public SWEmpireCouncilSphere CouncilSphere(EntityUid actor) =>
        GetFaction(actor) == "empire" && TryComp<SWDarkCouncilMemberComponent>(actor, out var member)
        && Enum.IsDefined(member.Sphere) ? member.Sphere : SWEmpireCouncilSphere.None;

    private string? ConsoleFaction(EntityUid uid)
    {
        if (HasComp<SWEmpireCouncilConsoleComponent>(uid)) return "empire";
        if (TryComp<SWStatisticsConsoleComponent>(uid, out var statistics))
            return statistics.Faction;
        if (HasComp<SWMinistryWorkConsoleComponent>(uid) || HasComp<SWSenateWorkConsoleComponent>(uid)
            || HasComp<SWSenateInfoConsoleComponent>(uid) || HasComp<SWSenateVoteConsoleComponent>(uid)
            || HasComp<SWDepartmentWorkConsoleComponent>(uid) || HasComp<SWBureauWorkConsoleComponent>(uid)
            || HasComp<SWJudicialWorkConsoleComponent>(uid) || HasComp<SWCourtInfoConsoleComponent>(uid)
            || HasComp<SWSectorSelectConsoleComponent>(uid) || HasComp<SWMinistrySelectConsoleComponent>(uid))
            return "republic";
        return null;
    }

    private void OnOpen(Entity<UserInterfaceComponent> ent, ref ActivatableUIOpenAttemptEvent args)
    {
        if (!CanRead(ent.Owner, args.User))
            args.Cancel();
    }

    private void OnMessage(BoundUserInterfaceMessageAttempt args)
    {
        if (!CanRead(args.Target, args.Actor))
            args.Cancel();
    }

    private void OnOpened(Entity<UserInterfaceComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (!CanRead(ent.Owner, args.Actor))
            _ui.CloseUi(ent.Owner, args.UiKey, args.Actor);
    }

    public override void Update(float frameTime)
    {
        // A faction can change on the same body without a player attachment event.
        // Only open strategic UIs are considered, and they are revoked before publication.
        var query = EntityQueryEnumerator<ActiveUserInterfaceComponent, UserInterfaceComponent>();
        while (query.MoveNext(out var uid, out _, out var ui))
        {
            if (ConsoleFaction(uid) is not { } faction)
                continue;
            foreach (var (key, actors) in ui.Actors.ToArray())
            foreach (var actor in actors.ToArray())
                if (!CanRead(uid, actor))
                    _ui.CloseUi((uid, ui), key, actor);
        }
    }
}
