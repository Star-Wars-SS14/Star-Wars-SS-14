using System.Linq;
using Content.Server.Chat.Managers;
using Content.Shared._SW.GlobalWar;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Server.Player;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server._SW.GlobalWar.Spawning;

public sealed partial class SWGlobalWarSpawnSystem
{
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IChatManager _chat = default!;

    private void InitializeAdmission()
    {
        SubscribeLocalEvent<SWGlobalWarOverflowJobsEvent>(OnOverflowJobs);
        SubscribeLocalEvent<SWGlobalWarSelectAdmissionEvent>(OnSelectAdmission);
    }

    private bool TryGetChosenFaction(NetUserId user, HumanoidCharacterProfile profile,
        SWGlobalWarSpawnConfigurationComponent configuration, out SWGlobalWarFaction faction)
    {
        if (_identity.TryGetMembership(user, out faction))
            return true;
        // Read the original preferences, before vacancies, bans or other restrictions remove any alternatives.
        var choices = profile.JobPriorities
            .Where(p => p.Value is JobPriority.Low or JobPriority.Medium or JobPriority.High &&
                        configuration.Roles.ContainsKey(p.Key))
            .Select(p => configuration.Roles[p.Key].Faction).Distinct().ToArray();
        faction = choices.Length == 1 ? choices[0] : default;
        return choices.Length == 1 && _identity.CanJoin(user, faction);
    }

    private bool TryGetCivilian(SWGlobalWarSpawnConfigurationComponent configuration,
        SWGlobalWarFaction faction, out ProtoId<JobPrototype> job, out EntityUid station)
    {
        job = default;
        station = default;
        return configuration.CivilianRoles.TryGetValue(faction, out job) &&
               configuration.Roles.TryGetValue(job, out var role) && role.Faction == faction && !role.Force &&
               _roleStations.TryGetValue(faction, out station) && !TerminatingOrDeleted(station);
    }

    private static HumanoidCharacterProfile OnlyJob(HumanoidCharacterProfile profile, ProtoId<JobPrototype> job)
    {
        return profile.WithJobPriorities(new Dictionary<ProtoId<JobPrototype>, JobPriority>
        {
            [job] = JobPriority.High,
        });
    }

    private void OnOverflowJobs(ref SWGlobalWarOverflowJobsEvent args)
    {
        if (!_rules.TryGetGlobalWarGamerule(out _))
            return;
        args.Handled = true;
        var configured = EnsureStations(out _, out var configuration);
        var groups = new Dictionary<SWGlobalWarFaction, Dictionary<NetUserId, HumanoidCharacterProfile>>();
        foreach (var user in args.Players)
        {
            if (args.AssignedJobs.TryGetValue(user, out var assigned) && assigned.Item1 != null)
                continue;
            args.AssignedJobs[user] = (null, EntityUid.Invalid);
            if (!configured || !args.Profiles.TryGetValue(user, out var profile) ||
                profile.PreferenceUnavailable != PreferenceUnavailableMode.SpawnAsOverflow)
                continue;
            if (!TryGetChosenFaction(user, profile, configuration, out var faction))
            {
                if (_players.TryGetSessionById(user, out var player))
                    _chat.DispatchServerMessage(player, Loc.GetString("sw-global-war-spawn-choose-faction"));
                continue;
            }
            if (!TryGetCivilian(configuration, faction, out var job, out _))
                continue;
            if (!groups.TryGetValue(faction, out var profiles))
                groups.Add(faction, profiles = new());
            profiles.Add(user, OnlyJob(profile, job));
        }
        foreach (var (faction, profiles) in groups)
        {
            var station = _roleStations[faction];
            // Reuse the normal candidate restrictions and finite-slot allocator, including earlier assignments.
            var fallback = _jobs.AssignJobs(profiles, [station], reservedJobs: args.AssignedJobs);
            foreach (var (user, assigned) in fallback)
                args.AssignedJobs[user] = assigned;
        }
    }

    private void OnSelectAdmission(ref SWGlobalWarSelectAdmissionEvent args)
    {
        if (!_rules.TryGetGlobalWarGamerule(out _))
            return;
        if (_respawn == null)
            _pendingJoins.Add(args.Player.UserId);
        args.Cancelled = true;
        args.Reason = SelectAdmission(ref args);
        if (args.Cancelled)
            NoteRefusal(args.Player, args.Reason);
    }

    /// <summary>Returns the refusal text, or clears the cancellation and sets the admitted role.</summary>
    private string SelectAdmission(ref SWGlobalWarSelectAdmissionEvent args)
    {
        if (!EnsureStations(out _, out var configuration) || _roleStations.Count == 0)
            return Loc.GetString("sw-global-war-spawn-unavailable");
        var profile = args.Profile;
        var user = args.Player.UserId;
        SWGlobalWarFaction faction;
        Dictionary<NetUserId, (ProtoId<JobPrototype>?, EntityUid)> selected;
        if (args.Job is { } requested)
        {
            if (!configuration.Roles.TryGetValue(requested, out var role) ||
                !_roleStations.TryGetValue(role.Faction, out var station) || args.Station != station)
                return Loc.GetString("sw-global-war-spawn-unavailable");
            if (!_identity.CanJoin(user, role.Faction))
                return Loc.GetString("sw-global-war-spawn-faction-locked");
            faction = role.Faction;
            selected = _jobs.AssignJobs(new() { [user] = OnlyJob(profile, requested) }, [station], false);
        }
        else
        {
            selected = _jobs.AssignJobs(new() { [user] = profile }, _roleStations.Values.ToArray(), false);
            var chosenFaction = TryGetChosenFaction(user, profile, configuration, out faction);
            if (!selected.ContainsKey(user) && !chosenFaction)
                return Loc.GetString("sw-global-war-spawn-choose-faction");
        }
        if (!selected.ContainsKey(user) && profile.PreferenceUnavailable == PreferenceUnavailableMode.SpawnAsOverflow &&
            TryGetCivilian(configuration, faction, out var civilian, out var civilianStation))
            selected = _jobs.AssignJobs(new() { [user] = OnlyJob(profile, civilian) }, [civilianStation], false);
        if (!selected.TryGetValue(user, out var result) || result.Item1 is not { } job)
            return AssignmentRefusal(args.Job);
        args.Job = job.Id;
        args.Station = result.Item2;
        args.Cancelled = false;
        return string.Empty;
    }

    private string AssignmentRefusal(string? requested)
    {
        if (requested == null ||
            _roleStations.Values.Any(s => _jobs.TryGetJobSlot(s, requested, out var slots) && slots == 0))
            return Loc.GetString("sw-global-war-spawn-no-slots");
        return Loc.GetString("sw-global-war-spawn-requirement");
    }
}
