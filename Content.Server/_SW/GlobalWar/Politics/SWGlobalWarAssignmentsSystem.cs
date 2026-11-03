using System.Linq;
using Content.Server.GameTicking;
using Content.Server._SW.GlobalWar.Identity;
using Content.Server._SW.GlobalWar.Spawning;
using Content.Server.Station.Systems;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Content.Shared.GameTicking;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Roles;
using Content.Shared.Preferences;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Server.Player;
using Robust.Shared.Enums;

namespace Content.Server._SW.GlobalWar.Politics;

/// <summary>Authoritative offices and permissions; ID cards and starting job history are never credentials.</summary>
public sealed partial class SWGlobalWarAssignmentsSystem : EntitySystem
{
    [Dependency] private readonly SWGlobalWarCampaignSystem _campaign = default!;
    [Dependency] private readonly SWGlobalWarIdentitySystem _identity = default!;
    [Dependency] private readonly SWGlobalWarSpawnSystem _spawn = default!;
    [Dependency] private readonly StationJobsSystem _jobs = default!;
    [Dependency] private readonly SharedMindSystem _minds = default!;
    [Dependency] private readonly MobStateSystem _mobs = default!;
    [Dependency] private readonly IPlayerManager _players = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWGlobalWarCampaignStartedEvent>(OnStarted);
        SubscribeLocalEvent<SWGlobalWarCampaignStoppedEvent>(OnStopped);
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
        InitializeSpawning();
        InitializeAdminActors();
        SubscribeLocalEvent<RulePlayerJobsAssignedEvent>(OnInitialAllocationComplete);
        _players.PlayerStatusChanged += OnPlayerStatusChanged;
    }

    public override void Shutdown()
    {
        _players.PlayerStatusChanged -= OnPlayerStatusChanged;
        base.Shutdown();
    }

    /// <summary>Resolves only the current campaign's registry; ending campaigns reject new commands.</summary>
    public bool TryGetRegistry(out Entity<SWGlobalWarAssignmentsComponent> registry)
    {
        registry = default;
        if (!_campaign.TryGetCampaign(out var campaign) || campaign.Comp.Phase == SWGlobalWarCampaignPhase.Ending ||
            !TryComp<SWGlobalWarAssignmentsComponent>(campaign.Owner, out var state) ||
            state.Generation != campaign.Comp.Generation)
            return false;
        registry = (campaign.Owner, state);
        return true;
    }

    /// <summary>Reads an immutable appointment, including disconnected incumbents.</summary>
    public bool TryGetAssignment(NetUserId user, out SWGlobalWarAssignment assignment)
    {
        assignment = default!;
        // Admin debugging: an active admin away from their own campaign body reads as the head of the local side.
        if (TryGetAdminActor(user, out assignment))
            return true;
        return TryGetRegistry(out var registry) && registry.Comp.Assignments.TryGetValue(user, out assignment!);
    }

    /// <summary>Reads the exact unfinished carrier. Death, disconnect and resignation do not release it.</summary>
    public bool TryGetForceCarrier(NetUserId user, out SWGlobalWarForceCarrier carrier)
    {
        carrier = default!;
        return TryGetRegistry(out var registry) && registry.Comp.ForceCarriers.TryGetValue(user, out carrier!);
    }

    /// <summary>Validates the connected owner's current living body and the exact office revision.</summary>
    public bool TryGetActor(ICommonSession session, Guid generation, long revision,
        out SWGlobalWarAssignment assignment)
    {
        assignment = default!;
        // Admin debugging: an aghost or any body of an active admin acts with the full rights of the local side.
        if (session.Status == SessionStatus.InGame && TryGetAdminActor(session.UserId, out assignment))
            return assignment.Generation == generation && assignment.Revision == revision;
        return session.Status == SessionStatus.InGame && session.AttachedEntity is { } body &&
               _identity.TryGetActor(session, body, out var mind, out var faction) && _mobs.IsAlive(body) &&
               TryGetAssignment(session.UserId, out assignment) && assignment.Generation == generation &&
               assignment.Revision == revision && assignment.Body == body && assignment.Mind == mind &&
               assignment.Faction == faction;
    }

    /// <summary>Derives authority from the current registry, including unoccupied ministry duties of the head.</summary>
    public SWGlobalWarAuthority GetAuthority(SWGlobalWarAssignment assignment)
    {
        if (!TryGetAssignment(assignment.User, out var current) || current != assignment)
            return SWGlobalWarAuthority.None;
        // Admin debugging: every office of the side at once.
        if (IsAdminActor(assignment))
            return Enum.GetValues<SWGlobalWarAuthority>().Aggregate(SWGlobalWarAuthority.None, (all, one) => all | one);
        var authority = assignment.Role switch
        {
            SWGlobalWarRole.Head => SWGlobalWarAuthority.Diplomacy | SWGlobalWarAuthority.Treasury,
            SWGlobalWarRole.WarMinister => SWGlobalWarAuthority.WarBudget | SWGlobalWarAuthority.MilitaryCommand,
            SWGlobalWarRole.LogisticsMinister => SWGlobalWarAuthority.LogisticsBudget,
            SWGlobalWarRole.IndustryMinister => SWGlobalWarAuthority.IndustryBudget,
            SWGlobalWarRole.SeniorOfficer or SWGlobalWarRole.JuniorOfficer => SWGlobalWarAuthority.MilitaryCommand,
            SWGlobalWarRole.Intelligence => SWGlobalWarAuthority.Intelligence,
            _ => SWGlobalWarAuthority.None,
        };
        if (assignment.Role != SWGlobalWarRole.Head || !TryGetRegistry(out var registry))
            return authority;
        foreach (var (role, permission) in new[]
                 {
                     (SWGlobalWarRole.WarMinister, SWGlobalWarAuthority.WarBudget | SWGlobalWarAuthority.MilitaryCommand),
                     (SWGlobalWarRole.LogisticsMinister, SWGlobalWarAuthority.LogisticsBudget),
                     (SWGlobalWarRole.IndustryMinister, SWGlobalWarAuthority.IndustryBudget),
                 })
        {
            if (!registry.Comp.Assignments.Values.Any(a => a.Faction == assignment.Faction && a.Role == role))
                authority |= permission;
        }
        return authority;
    }

    /// <summary>Direct-entry capacity. Internal recruitment is not limited by the five soldier entry places.</summary>
    public int? Available(SWGlobalWarFaction faction, SWGlobalWarRole role)
    {
        if (!TryGetRegistry(out var registry))
            return 0;
        var limit = role switch
        {
            SWGlobalWarRole.Civilian => (int?) null,
            SWGlobalWarRole.LogisticsWorker or SWGlobalWarRole.IndustryWorker => 3,
            SWGlobalWarRole.JuniorOfficer or SWGlobalWarRole.Pilot => 2,
            SWGlobalWarRole.Soldier => 5,
            _ => 1,
        };
        if (limit == null)
            return null;
        var occupied = role == SWGlobalWarRole.ForceUser
            ? registry.Comp.ForceCarriers.Values.Count(c => c.Faction == faction)
            : registry.Comp.Assignments.Values.Count(a => a.Faction == faction && a.Role == role &&
                (role != SWGlobalWarRole.Soldier || a.DirectEntry));
        return Math.Max(0, limit.Value - occupied - registry.Comp.Reservations.Values.Count(r =>
            r.Faction == faction && r.Role == role));
    }

    /// <summary>Reserves an entry before character creation. The token is not yet an appointment or authority.</summary>
    public bool TryReserveEntry(Guid generation, NetUserId user, ProtoId<JobPrototype> job,
        out SWGlobalWarAssignmentReservation reservation)
    {
        reservation = default!;
        // Admin debugging: an active admin entering again gives up the old office; that body stays as a plain NPC.
        if (TryGetRegistry(out var current) && current.Comp.Generation == generation &&
            current.Comp.Assignments.TryGetValue(user, out var held) && IsActiveAdmin(user, out _))
            Remove(current, held);
        if (!TryGetRegistry(out var registry) || registry.Comp.Generation != generation ||
            !TryGetRole(registry.Owner, job, out var faction, out var role) || !_identity.CanJoin(user, faction) ||
            registry.Comp.Assignments.ContainsKey(user) || registry.Comp.ForceCarriers.ContainsKey(user))
            return false;
        var existing = registry.Comp.Reservations.Values.FirstOrDefault(r => r.User == user);
        if (existing != null)
        {
            reservation = existing;
            return existing.Job == job;
        }
        if (Available(faction, role) == 0)
            return false;
        if (role == SWGlobalWarRole.Head && !HeadEntryOpen(registry, faction))
            return false;
        reservation = new SWGlobalWarAssignmentReservation(Guid.NewGuid(), generation, user, faction, role, job);
        registry.Comp.Reservations.Add(reservation.Id, reservation);
        Changed(registry);
        return true;
    }

    /// <summary>Releases only this reservation in this generation. Repeated cleanup cannot release a successor.</summary>
    public void ReleaseEntry(Guid generation, Guid token)
    {
        if (TryGetRegistry(out var registry) && registry.Comp.Generation == generation &&
            registry.Comp.Reservations.Remove(token))
            Changed(registry);
    }

    /// <summary>Commits only after B has enrolled the actual player's successfully created mind and body.</summary>
    private bool CommitEntry(SWGlobalWarAssignmentReservation reservation, EntityUid body, HumanoidCharacterProfile profile)
    {
        if (!TryGetRegistry(out var registry) || registry.Comp.Generation != reservation.Generation ||
            !registry.Comp.Reservations.TryGetValue(reservation.Id, out var current) || current != reservation ||
            !_identity.TryGetIdentity(body, out var mind, out var faction) || faction != reservation.Faction ||
            Comp<MindComponent>(mind).UserId != reservation.User || registry.Comp.Assignments.ContainsKey(reservation.User))
            return false;
        var snapshot = EnsureComp<SWGlobalWarCharacterProfileComponent>(body);
        snapshot.Generation = reservation.Generation;
        snapshot.User = reservation.User;
        snapshot.Mind = mind;
        snapshot.Profile = profile.Clone();
        registry.Comp.Reservations.Remove(reservation.Id);
        registry.Comp.Assignments.Add(reservation.User, new SWGlobalWarAssignment(Guid.NewGuid(), reservation.Generation,
            reservation.User, mind, body, faction, reservation.Role, reservation.Job, ++registry.Comp.Revision, true));
        if (reservation.Role == SWGlobalWarRole.ForceUser)
            registry.Comp.ForceCarriers.Add(reservation.User, new SWGlobalWarForceCarrier(Guid.NewGuid(),
                reservation.Generation, reservation.User, mind, body, faction));
        if (reservation.Role == SWGlobalWarRole.Head)
            registry.Comp.Successions.Remove(faction);
        Changed(registry);
        return true;
    }

    private bool TryGetRole(EntityUid rule, ProtoId<JobPrototype> job,
        out SWGlobalWarFaction faction, out SWGlobalWarRole role)
    {
        faction = default;
        role = default;
        if (!TryComp<SWGlobalWarSpawnConfigurationComponent>(rule, out var catalog) ||
            !catalog.Roles.TryGetValue(job, out var entry) || entry.Role is not { } kind)
            return false;
        faction = entry.Faction;
        role = kind;
        return faction is SWGlobalWarFaction.Republic or SWGlobalWarFaction.Empire &&
               entry.Force == (kind == SWGlobalWarRole.ForceUser);
    }

    private void OnStarted(ref SWGlobalWarCampaignStartedEvent args)
    {
        var registry = EnsureComp<SWGlobalWarAssignmentsComponent>(args.Rule);
        if (registry.Generation == args.Generation)
            return;
        Clear(registry);
        registry.Generation = args.Generation;
        if (TryComp<SWGlobalWarSpawnConfigurationComponent>(args.Rule, out var catalog))
        {
            var unique = new HashSet<(SWGlobalWarFaction, SWGlobalWarRole)>();
            foreach (var (job, entry) in catalog.Roles)
            {
                if (entry.Role != null && (!TryGetRole(args.Rule, job, out var faction, out var role) ||
                    !unique.Add((faction, role))))
                    throw new InvalidOperationException("Campaign jobs require a unique, consistent faction/office mapping.");
            }
        }
    }

    private void OnStopped(ref SWGlobalWarCampaignStoppedEvent args)
    {
        if (TryComp<SWGlobalWarAssignmentsComponent>(args.Rule, out var registry) &&
            registry.Generation == args.Generation)
            Clear(registry);
    }

    private void Clear(SWGlobalWarAssignmentsComponent registry)
    {
        registry.Assignments.Clear();
        registry.Reservations.Clear();
        registry.ForceCarriers.Clear();
        registry.Offers.Clear();
        registry.CompletedOffers.Clear();
        registry.HeadDisconnected.Clear();
        registry.Successions.Clear();
        registry.InitialAllocationComplete = false;
        registry.Requests.Clear();
        registry.Revision = 0;
        registry.NextCheck = TimeSpan.Zero;
    }

    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead || !TryGetRegistry(out var registry))
            return;
        foreach (var assignment in registry.Comp.Assignments.Values.Where(a => a.Body == args.Target).ToArray())
            Remove(registry, assignment);
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (!TryGetRegistry(out var registry) || !_campaign.TryGetCampaign(out var campaign) ||
            !registry.Comp.Assignments.TryGetValue(args.Session.UserId, out var assignment) ||
            assignment.Role != SWGlobalWarRole.Head)
            return;
        var now = _campaign.Elapsed(campaign);
        // Reconnecting after the deadline cannot outrun the periodic cleanup tick.
        if (registry.Comp.HeadDisconnected.TryGetValue(assignment.Id, out var since) &&
            now - since >= campaign.Comp.LeaderDisconnectReserve)
        {
            Remove(registry, assignment);
            return;
        }
        if (args.NewStatus == SessionStatus.Disconnected)
            registry.Comp.HeadDisconnected.TryAdd(assignment.Id, now);
        else if (args.NewStatus == SessionStatus.InGame)
            registry.Comp.HeadDisconnected.Remove(assignment.Id);
    }

    public override void Update(float frameTime)
    {
        if (!TryGetRegistry(out var registry) || !_campaign.TryGetCampaign(out var campaign))
            return;
        var now = _campaign.Elapsed(campaign);
        if (now < registry.Comp.NextCheck)
            return;
        registry.Comp.NextCheck = now + TimeSpan.FromSeconds(1);
        foreach (var assignment in registry.Comp.Assignments.Values.ToArray())
        {
            if (TerminatingOrDeleted(assignment.Body) || _mobs.IsDead(assignment.Body))
            {
                Remove(registry, assignment);
                continue;
            }
            if (assignment.Role != SWGlobalWarRole.Head)
                continue;
            if (_players.TryGetSessionById(assignment.User, out var session) && session.Status == SessionStatus.InGame)
            {
                registry.Comp.HeadDisconnected.Remove(assignment.Id);
                continue;
            }
            if (!registry.Comp.HeadDisconnected.TryGetValue(assignment.Id, out var since))
                registry.Comp.HeadDisconnected.Add(assignment.Id, now);
            else if (now - since >= campaign.Comp.LeaderDisconnectReserve)
                Remove(registry, assignment);
        }
        foreach (var faction in registry.Comp.Successions.Keys.ToArray())
            AdvanceSuccession(registry, faction);
    }

    private void Remove(Entity<SWGlobalWarAssignmentsComponent> registry, SWGlobalWarAssignment assignment)
    {
        if (!registry.Comp.Assignments.TryGetValue(assignment.User, out var current) || current != assignment)
            return;
        registry.Comp.Assignments.Remove(assignment.User);
        registry.Comp.HeadDisconnected.Remove(assignment.Id);
        if (assignment.Role == SWGlobalWarRole.Head)
            BeginSuccession(registry, assignment.Faction);
        Changed(registry);
    }

    private void Changed(Entity<SWGlobalWarAssignmentsComponent> registry)
    {
        registry.Comp.Revision++;
        ProjectJobs(registry);
        if (_personnelTransaction)
        {
            _pendingNotification = registry;
            return;
        }
        NotifyAssignments(registry);
    }

    private void NotifyAssignments(Entity<SWGlobalWarAssignmentsComponent> registry)
    {
        var changed = new SWGlobalWarAssignmentsChangedEvent(registry.Comp.Generation, registry.Comp.Revision);
        RaiseLocalEvent(ref changed);
    }
}
