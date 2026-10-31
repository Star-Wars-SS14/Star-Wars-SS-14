using System.Linq;
using Content.Server._SW.Autobalance;
using Content.Server.GameTicking;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Shared._SW.TaskSystem;
using Content.Shared._SW.Tutorial;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._SW.Tutorial;

/// <summary>
/// After the last step: the player goes back to the lobby, picks a side, and enters the real round through the
/// ordinary spawn path, with its slot, autobalance and arrival radio.
/// </summary>
public sealed partial class SWTutorialSystem
{
    [Dependency] private readonly StationJobsSystem _stationJobs = default!;
    [Dependency] private readonly SWAutobalanceGameruleSystem _autobalance = default!;

    private const string RepublicFaction = "RepublicFaction";
    private const string EmpireFaction = "EmpireFaction";
    private const string RepublicSoldier = "SWRepublicSoldier";
    private const string EmpireSoldier = "SWEmpireSoldier";

    /// <summary>
    /// Finished the tutorial this round and has not used the extra slot yet.
    /// </summary>
    private readonly HashSet<NetUserId> _graduates = new();

    private void InitializeFinish()
    {
        SubscribeNetworkEvent<SWTutorialJoinSideEvent>(OnJoinSide);
    }

    private void Graduate(ICommonSession session, Student student)
    {
        Funnel(student, $"finish totalsecs={(_timing.CurTime - student.StartedAt).TotalSeconds:F1}");
        SetFlag(session, SWTutorialTrackers.Done);
        _graduates.Add(session.UserId);
        Leave(session, student);

        var (republic, empire) = CountAlive();
        RaiseNetworkEvent(new SWTutorialFinishedEvent(_ticker.RunLevel == GameRunLevel.InRound, republic, empire), session);
    }

    private (int Republic, int Empire) CountAlive()
    {
        var republic = 0;
        var empire = 0;
        var query = EntityQueryEnumerator<SWFactionMemberComponent, MobStateComponent>();
        while (query.MoveNext(out _, out var member, out var state))
        {
            if (state.CurrentState != MobState.Alive)
                continue;

            if (member.Faction == RepublicFaction)
                republic++;
            else if (member.Faction == EmpireFaction)
                empire++;
        }

        return (republic, empire);
    }

    private void OnJoinSide(SWTutorialJoinSideEvent ev, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;
        if (_ticker.RunLevel != GameRunLevel.InRound
            || IsStudent(session.UserId)
            || _ticker.UserHasJoinedGame(session))
        {
            return;
        }

        var job = ev.Side == SWTutorialSide.Republic ? RepublicSoldier : EmpireSoldier;
        var query = EntityQueryEnumerator<StationJobsComponent, StationSpawningComponent>();
        if (!query.MoveNext(out var station, out _, out _))
            return;

        var hasSlot = _stationJobs.TryGetJobSlot(station, job, out var slots) && slots is null or > 0;
        if (!hasSlot && !TryUseGraduateSlot(session, station, job, ev.Side))
        {
            _chat.DispatchServerMessage(session, Loc.GetString("sw-tutorial-error-no-slot"));
            return;
        }

        _graduates.Remove(session.UserId);
        _ticker.MakeJoinGame(session, station, job);
    }

    /// <summary>
    /// Slots are lives and do not come back, so a long round can leave a graduate with nothing but a civilian. One
    /// extra slot on the first life, on the side with fewer living, keeps the tutorial from ending that way.
    /// </summary>
    private bool TryUseGraduateSlot(ICommonSession session, EntityUid station, string job, SWTutorialSide side)
    {
        if (!_cfg.GetCVar(SWVars.TutorialGraduateExtraSlot) || !_graduates.Contains(session.UserId))
            return false;

        var (republic, empire) = CountAlive();
        var isWeaker = side == SWTutorialSide.Republic ? republic <= empire : empire <= republic;
        if (!isWeaker)
            return false;

        _autobalance.GrantExtraSlot(station, job);
        return true;
    }
}
