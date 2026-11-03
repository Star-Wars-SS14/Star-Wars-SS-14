using System.Linq;
using Content.Server._SW.GlobalWar.Spawning;
using Content.Shared.GameTicking;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;
using Content.Shared._SW.GlobalWar.Politics;

namespace Content.Server._SW.GlobalWar.Politics;

public sealed partial class SWGlobalWarAssignmentsSystem
{
    private void InitializeSpawning()
    {
        SubscribeLocalEvent<SWGlobalWarRoleStationsReadyEvent>(OnStationsReady);
        SubscribeLocalEvent<SWGlobalWarValidateSpawnEvent>(OnValidateSpawn, after: [typeof(SWGlobalWarSpawnSystem)]);
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnSpawnComplete, after: [typeof(SWGlobalWarSpawnSystem)]);
        SubscribeLocalEvent<SWGlobalWarSpawnFinishedEvent>(OnSpawnFinished);
        SubscribeLocalEvent<SWGlobalWarAssignJobEvent>(OnAssignJob);
        SubscribeLocalEvent<SWGlobalWarRoundStartJobsEvent>(OnRoundStartJobs);
        SubscribeLocalEvent<SWGlobalWarJobAllocationEvent>(OnJobAllocation);
    }

    private void OnStationsReady(ref SWGlobalWarRoleStationsReadyEvent args)
    {
        if (TryGetRegistry(out var registry) && registry.Owner == args.Rule && registry.Comp.Generation == args.Generation)
            ProjectJobs(registry);
    }

    private void ProjectJobs(Entity<SWGlobalWarAssignmentsComponent> registry)
    {
        if (!TryComp<SWGlobalWarSpawnConfigurationComponent>(registry.Owner, out var catalog))
            return;
        foreach (var (job, entry) in catalog.Roles)
        {
            if (entry.Role is not { } role || !_spawn.TryGetRoleStation(entry.Faction, out var station))
                continue;
            var available = Available(entry.Faction, role);
            if (role == SWGlobalWarRole.Head && registry.Comp.Successions.TryGetValue(entry.Faction, out var succession) &&
                succession.Stage < 2)
                available = 0;
            if (available is { } finite)
                _jobs.TrySetJobSlot(station, job.Id, finite, true);
            else
                _jobs.MakeJobUnlimited(station, job.Id);
        }
    }

    private bool Managed(Entity<SWGlobalWarAssignmentsComponent> registry, EntityUid station, string job)
    {
        return TryGetRole(registry.Owner, job, out var faction, out _) &&
               _spawn.TryGetRoleStation(faction, out var expected) && expected == station;
    }

    private void OnValidateSpawn(ref SWGlobalWarValidateSpawnEvent args)
    {
        if (args.Cancelled || !TryGetRegistry(out var registry) || !Managed(registry, args.Station, args.Job))
            return;
        if (!TryReserveEntry(registry.Comp.Generation, args.Player.UserId, args.Job, out _))
        {
            args.Cancelled = true;
            args.Reason = Loc.GetString("sw-global-war-spawn-no-slots");
        }
    }

    private void OnSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        if (!TryGetRegistry(out var registry) || args.JobId == null || !Managed(registry, args.Station, args.JobId))
            return;
        var reservation = registry.Comp.Reservations.Values.FirstOrDefault(r => r.User == args.Player.UserId);
        if (reservation == null || !CommitEntry(reservation, args.Mob, args.Profile))
            throw new InvalidOperationException("Campaign appointment requires the committed identity and entry reservation.");
    }

    private void OnSpawnFinished(ref SWGlobalWarSpawnFinishedEvent args)
    {
        if (!TryGetRegistry(out var registry))
            return;
        var user = args.User;
        var job = args.Job;
        foreach (var reservation in registry.Comp.Reservations.Values.Where(r => r.User == user && r.Job == job).ToArray())
            ReleaseEntry(reservation.Generation, reservation.Id);
    }

    private void OnAssignJob(ref SWGlobalWarAssignJobEvent args)
    {
        if (!TryGetRegistry(out var registry) || !Managed(registry, args.Station, args.Job))
            return;
        args.Handled = true;
        var user = args.User;
        var job = args.Job;
        args.Allowed = registry.Comp.Reservations.Values.Any(r => r.User == user && r.Job == job);
    }

    private void OnRoundStartJobs(ref SWGlobalWarRoundStartJobsEvent args)
    {
        if (!TryGetRegistry(out var registry) ||
            !TryComp<SWGlobalWarSpawnConfigurationComponent>(registry.Owner, out var catalog))
            return;
        var station = args.Station;
        var matching = catalog.Roles.Where(r => r.Value.Role != null && Managed(registry, station, r.Key.Id)).ToArray();
        if (matching.Length == 0)
            return;
        args.Jobs = _jobs.GetJobs(station).ToDictionary(p => p.Key, p => p.Value);
        foreach (var (job, entry) in matching)
            args.Jobs[job] = Available(entry.Faction, entry.Role!.Value);
    }

    private void OnJobAllocation(ref SWGlobalWarJobAllocationEvent args)
    {
        if (!TryGetRegistry(out var registry) || args.PriorAllocations == null)
            return;
        foreach (var (user, (job, station)) in args.PriorAllocations)
        {
            if (job == null || !Managed(registry, station, job.Value.Id))
                continue;
            if (registry.Comp.Reservations.Values.Any(r => r.User == user && r.Job == job.Value) ||
                registry.Comp.Assignments.TryGetValue(user, out var assignment) && assignment.Job == job.Value)
                args.AlreadyCounted.Add(user);
        }
    }
}
