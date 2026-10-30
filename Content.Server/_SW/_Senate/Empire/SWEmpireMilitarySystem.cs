using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server._SW._Senate.Core;
using Content.Server._SW._Senate.Economy;
using Content.Server._SW._Senate.Economy.Simulation;
using Content.Shared._SW._Senate.Economy;
using Content.Shared._SW._Senate.Empire;
using Content.Shared._SW._Senate.Sectors;
using Content.Shared.GameTicking;
using Robust.Shared.Prototypes;

namespace Content.Server._SW._Senate.Empire;

/// <summary>Imperial formations and their physical movements; combat is shared with Defense.</summary>
public sealed partial class SWEmpireMilitarySystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly SWEmpireEconomySystem _economy = default!;
    [Dependency] private readonly SWRepublicEconomySystem _republicEconomy = default!;
    [Dependency] private readonly SWMinistryDefenseSystem _defense = default!;
    [Dependency] private readonly SWEmpireDiplomacySystem _diplomacy = default!;
    private readonly Dictionary<int, Fleet> _fleets = new();
    private readonly Dictionary<string, SWGalacticMapWorld> _worlds = new();
    private readonly List<SWTransitLinkPrototype> _links = new();
    private readonly Dictionary<string, string> _supplyPorts = new();
    private readonly Dictionary<string, string> _construction = new();
    private readonly Dictionary<string, SWEmpireMilitaryObservationView> _observations = new();
    private readonly Dictionary<string, int> _raidTargets = new();
    private readonly HashSet<int> _securedRaids = new();
    private readonly HashSet<int> _raidTickedThisUpdate = new();
    private readonly HashSet<string> _announcedSystemsThisUpdate = new();
    private readonly HashSet<string> _requests = new();
    private bool _initialized;
    private int _nextFleet = 1, _nextProject = 1;

    private sealed class Fleet
    {
        public int Id;
        public string Name = string.Empty, Home = string.Empty, World = string.Empty, Target = string.Empty;
        public string? Next, WorkProject, Crossing, CivilianBase, BaseSupplyWorld, BlockedLeg;
        public int EventId, RaidEventId;
        public bool RaidUsesBorder;
        public int EscortLeader;
        public string? Permit;
        public string? CargoGood, RaidPending, RaidGood, RaidWorld, DepositPending;
        public double CargoAmount, RaidReadyAt;
        public bool RaidAttempted;
        public readonly Queue<string> Path = new();
        public SWEmpireMilitaryAction Order = SWEmpireMilitaryAction.Guard;
        public double Strength = 90, Damage, Supply = 80, Training = 45, Until, LegStartedAt, BaseSupplyRemaining;
        public bool Alive => Strength > 0 && Damage < 100;
    }

    public override void Initialize()
    {
        UpdatesAfter.Add(typeof(SWEmpireEconomySystem));
        UpdatesBefore.Add(typeof(SWSenateTimeSystem));
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => Reset());
    }

    private void Reset()
    {
        _initialized = false; _nextFleet = 1; _nextProject = 1;
        _fleets.Clear(); _worlds.Clear(); _links.Clear(); _supplyPorts.Clear(); _construction.Clear(); _observations.Clear(); _raidTargets.Clear(); _securedRaids.Clear(); _raidTickedThisUpdate.Clear(); _announcedSystemsThisUpdate.Clear(); _requests.Clear();
    }

    private void EnsureInitialized()
    {
        if (_initialized || _economy.Definition == null) return;
        _initialized = true;
        var map = _proto.Index<SWGalacticMapPrototype>("SenateRepublic");
        foreach (var world in map.Worlds) _worlds[world.Id] = world;
        _links.AddRange(_proto.EnumeratePrototypes<SWTransitLinkPrototype>().Where(l => _worlds.ContainsKey(l.From) && _worlds.ContainsKey(l.To)));
        var homes = _economy.Definition.Worlds.Where(w => w.Active && _worlds.ContainsKey(w.Id))
            .OrderBy(w => w.Id).Select(w => w.Id).ToArray();
        for (var i = 0; i < Math.Min(5, homes.Length); i++)
        {
            var home = homes[i * homes.Length / Math.Min(5, homes.Length)];
            var id = _nextFleet++;
            _fleets[id] = new Fleet { Id = id, Name = $"{id}-е имперское соединение", Home = home, World = home, Target = home };
        }
    }

    private bool Own(string world) => _economy.Definition?.Worlds.Any(w => w.Id == world && w.Active) == true;
    private bool Imperial(string world) => _worlds.TryGetValue(world, out var w) && w.Faction == "empire";
    private SWTransitLinkPrototype? Link(string from, string to) => _links.FirstOrDefault(l =>
        l.From == from && l.To == to || l.From == to && l.To == from);

    private List<string>? OwnPath(string from, string target)
    {
        if (!Imperial(from) || !Imperial(target)) return null;
        var distances = new Dictionary<string, double> { [from] = 0 };
        var previous = new Dictionary<string, string>();
        var remaining = _worlds.Keys.Where(Imperial).ToHashSet();
        while (remaining.Count > 0)
        {
            var current = remaining.Where(distances.ContainsKey).OrderBy(x => distances[x]).FirstOrDefault();
            if (current == null) break;
            remaining.Remove(current);
            if (current == target)
            {
                var path = new List<string>();
                while (current != from) { path.Add(current); current = previous[current]; }
                path.Reverse(); return path;
            }
            foreach (var edge in _links.Where(l => l.From == current || l.To == current))
            {
                var next = edge.From == current ? edge.To : edge.From;
                if (!remaining.Contains(next)) continue;
                var cost = distances[current] + Math.Clamp(edge.TravelHours * .4, .1, .3);
                if (distances.TryGetValue(next, out var old) && old <= cost) continue;
                distances[next] = cost; previous[next] = current;
            }
        }
        return null;
    }

    // A basing agreement grants peaceful access only to the named endpoint, over
    // a real accessible edge. It does not add the world to the Imperial economy.
    private List<string>? PeacefulPath(Fleet fleet, string from, string target, bool returning)
    {
        if (Imperial(from) && Imperial(target)) return OwnPath(from, target);
        if (returning && from == fleet.CivilianBase && Imperial(target))
        {
            foreach (var edge in _links.Where(l => l.Accessible && (l.From == from || l.To == from)))
            {
                var neighbour = edge.From == from ? edge.To : edge.From;
                var rest = OwnPath(neighbour, target);
                if (rest == null) continue;
                rest.Insert(0, neighbour); return rest;
            }
            return null;
        }
        if (!returning && Imperial(from) && _diplomacy.AllowsCivilianBasing(target))
        {
            foreach (var edge in _links.Where(l => l.Accessible && (l.From == target || l.To == target)))
            {
                var neighbour = edge.From == target ? edge.To : edge.From;
                var rest = OwnPath(from, neighbour);
                if (rest == null) continue;
                rest.Add(target); return rest;
            }
        }
        return null;
    }

    private void TickSupply(Fleet fleet, double hours)
    {
        if (fleet.Next == null && fleet.World == fleet.CivilianBase && !Own(fleet.World))
        {
            if (!_diplomacy.AllowsCivilianBasing(fleet.World)) return;
            if (fleet.BaseSupplyWorld != null)
            {
                if (fleet.BaseSupplyWorld != fleet.World) return;
                fleet.BaseSupplyRemaining = Math.Max(0, fleet.BaseSupplyRemaining - hours);
                if (fleet.BaseSupplyRemaining <= 0)
                { fleet.Supply = Math.Min(100, fleet.Supply + SWRepublicNavyLogistics.SupplyHours * 3); fleet.BaseSupplyWorld = null; }
                return;
            }
            if (fleet.Supply > 60) return;
            var kit = SWRepublicNavyLogistics.CreateProject("base-supply", fleet.World, SWNavyProjectKind.Supply);
            var needed = kit.Materials.ToDictionary(m => m.Good, m => m.Quantity);
            if (_diplomacy.TryConsumeBaseSupplies(fleet.World, needed))
            { fleet.BaseSupplyWorld = fleet.World; fleet.BaseSupplyRemaining = kit.WorkHours; }
            return;
        }
        if (fleet.Supply > 60 || fleet.Next != null || !Own(fleet.World)) return;
        if (_supplyPorts.TryGetValue(fleet.World, out var id))
        {
            if (_economy.GetProject(id) is { Status: EconomyProjectStatus.Completed })
            { fleet.Supply = Math.Min(100, fleet.Supply + SWRepublicNavyLogistics.SupplyHours * 3); _supplyPorts.Remove(fleet.World); }
            return;
        }
        id = $"imperial-navy-supply-{_nextProject++}";
        if (_economy.QueueNavyProject(id, fleet.World, SWNavyProjectKind.Supply)) _supplyPorts[fleet.World] = id;
    }

    public override void Update(float frameTime)
    {
        var hours = _time.ActiveSeconds(frameTime) / SWSenateTimeSystem.RealSecondsPerGameHour;
        if (hours <= 0) return;
        EnsureInitialized();
        _raidTickedThisUpdate.Clear();
        _announcedSystemsThisUpdate.Clear();
        foreach (var (id, world) in _construction.ToArray())
        {
            if (_economy.GetProject(id) is not { Status: EconomyProjectStatus.Completed }) continue;
            var fleetId = _nextFleet++;
            _fleets[fleetId] = new Fleet { Id = fleetId, Name = $"{fleetId}-е имперское соединение", Home = world,
                World = world, Target = world, Strength = 70, Supply = 12, Training = 10 };
            _construction.Remove(id);
        }
        foreach (var fleet in _fleets.Values.Where(f => f.Alive))
        {
            if (fleet.EventId == 0 && fleet.Permit != null && !CrossingOpen(fleet.Permit)) fleet.Permit = null;
            if (fleet.WorkProject != null && fleet.Next == null && Own(fleet.World)
                && _economy.GetProject(fleet.WorkProject) is { Status: EconomyProjectStatus.Completed } repair && repair.World == fleet.World)
            { fleet.Damage = 0; fleet.Strength = Math.Min(100, fleet.Strength + 30); fleet.WorkProject = null; fleet.Order = SWEmpireMilitaryAction.Guard; }
            fleet.Supply = Math.Max(0, fleet.Supply - hours * (fleet.Next == null ? 3 : 7));
            var forbidden = HostilitiesBlocked(fleet);
            if (forbidden && fleet.RaidPending != null) _republicEconomy.Runner?.CancelExternalTransfer(fleet.RaidPending);
            TickRaidCargo(fleet);
            // Resolve the completed leg before looking for a real port kit. Otherwise a
            // patrol immediately starts its next leg and is never seen in port by TickSupply.
            if (fleet.Next != null && _time.ActiveGameHours >= fleet.Until)
            {
                var previous = fleet.World;
                fleet.World = fleet.Next; fleet.Next = null;
                AnnounceHostileArrival(fleet, previous);
            }
            TickSupply(fleet, hours);
            TickEscort(fleet);
            if (!forbidden) { TickBorderBattle(fleet, hours); TickRaidBattle(fleet, hours); }
            ObserveBorder(fleet);
            if (fleet.Next == null && Republic(fleet.World) && fleet.Permit == null && fleet.Order is SWEmpireMilitaryAction.Raid or SWEmpireMilitaryAction.Blockade)
                QueueRetreat(fleet);
            if (fleet.Order == SWEmpireMilitaryAction.Blockade && Republic(fleet.World))
            {
                var owner = fleet.EventId != 0 ? fleet : _fleets.Values.FirstOrDefault(f => f.EventId != 0 && f.Permit == fleet.Crossing);
                if (owner != null) _defense.TrySetImperialBlockade(owner.EventId, !forbidden && _defense.ImperialOperationsAllowed);
            }
            if (fleet.Next == null && fleet.Path.Count == 0 && fleet.Order == SWEmpireMilitaryAction.Train && Own(fleet.World))
                fleet.Training = Math.Min(100, fleet.Training + hours * 2);
            if (fleet.Next == null && fleet.Path.Count == 0 && fleet.Order == SWEmpireMilitaryAction.Patrol && Imperial(fleet.World))
            {
                var patrolEdge = _links.Where(l => l.From == fleet.World && Imperial(l.To) || l.To == fleet.World && Imperial(l.From))
                    .OrderBy(l => l.ID).FirstOrDefault();
                if (patrolEdge != null) fleet.Path.Enqueue(patrolEdge.From == fleet.World ? patrolEdge.To : patrolEdge.From);
            }
            // A recall may be queued, but the fleet stays at the loading port until the
            // worker acknowledges whether cargo was physically taken.
            if (fleet.Next != null || fleet.RaidPending != null || fleet.Path.Count == 0) continue;
            if (forbidden && fleet.Order != SWEmpireMilitaryAction.Return) continue;
            // A minister vacancy freezes the operation, not its remaining route.
            if (fleet.Order != SWEmpireMilitaryAction.Return && !_defense.ImperialOperationsAllowed
                && (Republic(fleet.World) || Republic(fleet.Path.Peek()))) continue;
            if (fleet.Order == SWEmpireMilitaryAction.Escort && Imperial(fleet.World) && Republic(fleet.Path.Peek())
                && !CrossingPassable(fleet.Crossing)) continue;
            var next = fleet.Path.Dequeue(); var edge = Link(fleet.World, next);
            if (edge == null || !(Imperial(fleet.World) && Imperial(next)
                || edge.Accessible && fleet.Order == SWEmpireMilitaryAction.Move && Imperial(fleet.World)
                    && next == fleet.CivilianBase && _diplomacy.AllowsCivilianBasing(next)
                || edge.Accessible && fleet.Order == SWEmpireMilitaryAction.Return && fleet.World == fleet.CivilianBase && Imperial(next)
                || edge.Accessible && Republic(fleet.World) && Republic(next) && (fleet.Order == SWEmpireMilitaryAction.Return || fleet.Permit != null && _defense.ImperialOperationsAllowed)
                || fleet.Order == SWEmpireMilitaryAction.Escort && edge.ID == fleet.Crossing && Imperial(fleet.World)
                    && Republic(next) && _defense.ImperialOperationsAllowed && CrossingPassable(edge.ID)
                || fleet.Order == SWEmpireMilitaryAction.Return && edge.ID == fleet.Crossing && Republic(fleet.World) && Imperial(next)))
            { fleet.Path.Clear(); continue; }
            fleet.Next = next; fleet.LegStartedAt = _time.ActiveGameHours;
            fleet.Until = fleet.LegStartedAt + Math.Clamp(edge.TravelHours * .4, .1, .3);
        }
    }
}
