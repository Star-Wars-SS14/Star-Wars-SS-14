using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Mapping;
using Content.Shared.GameTicking;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server._SW.GlobalWar.Mapping;

/// <summary>Resolves stable map addresses without loading maps or guessing nearby devices.</summary>
public sealed class SWGlobalWarMapAnchorSystem : EntitySystem
{
    [Dependency] private readonly SWGlobalWarGameruleSystem _rules = default!;
    [Dependency] private readonly MetaDataSystem _metadata = default!;

    private readonly Dictionary<(EntityUid, ProtoId<SWGlobalWarPlanetPrototype>, SWGlobalWarPlanetLocation, string),
        HashSet<EntityUid>> _anchors = new();
    private readonly Dictionary<EntityUid, (EntityUid Rule, ProtoId<SWGlobalWarPlanetPrototype> Sector,
        SWGlobalWarPlanetLocation Location, string Id)>
        _addresses = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWGlobalWarMapAnchorComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<SWGlobalWarMapAnchorComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<SWGlobalWarMapAnchorComponent, MapUidChangedEvent>(OnMoved);
        SubscribeLocalEvent<SWGlobalWarMapAnchorComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<SWGlobalWarLocationReadyEvent>(OnReady);
        SubscribeLocalEvent<SWGlobalWarPlanetStoppedEvent>(OnStopped);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => Clear());
    }

    private void OnInit(Entity<SWGlobalWarMapAnchorComponent> ent, ref ComponentInit args)
    {
        _metadata.AddFlag(ent.Owner, MetaDataFlags.ExtraTransformEvents);
    }

    private void OnMapInit(Entity<SWGlobalWarMapAnchorComponent> ent, ref MapInitEvent args)
    {
        Register(ent);
    }

    private void OnMoved(Entity<SWGlobalWarMapAnchorComponent> ent, ref MapUidChangedEvent args)
    {
        Register(ent);
    }

    private void OnShutdown(Entity<SWGlobalWarMapAnchorComponent> ent, ref ComponentShutdown args)
    {
        Unregister(ent.Owner);
    }

    private void OnReady(ref SWGlobalWarLocationReadyEvent args)
    {
        var query = AllEntityQuery<SWGlobalWarMapAnchorComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var anchor, out var transform))
        {
            if (transform.MapUid == args.Map)
                Register((uid, anchor));
        }
    }

    private void Register(Entity<SWGlobalWarMapAnchorComponent> ent)
    {
        Unregister(ent.Owner);
        if (TerminatingOrDeleted(ent.Owner) || string.IsNullOrWhiteSpace(ent.Comp.AnchorId) ||
            !_rules.TryGetGlobalWarGamerule(out var rule) ||
            !TryComp<SWGlobalWarPlanetMapComponent>(Transform(ent.Owner).MapUid, out var map) ||
            !TryComp<SWGlobalWarPlanetComponent>(map.Planet, out var planet) || planet.GlobalWarGamerule != rule.Owner)
            return;
        var physicalMap = Transform(ent.Owner).MapUid;
        ent.Comp.RegisteredMap ??= physicalMap;
        if (ent.Comp.RegisteredMap != physicalMap)
            return;
        var address = (rule.Owner, planet.Prototype, map.Location, ent.Comp.AnchorId);
        if (!_anchors.TryGetValue(address, out var entries))
            _anchors.Add(address, entries = new HashSet<EntityUid>());
        entries.Add(ent.Owner);
        _addresses.Add(ent.Owner, address);
    }

    private void OnStopped(ref SWGlobalWarPlanetStoppedEvent args)
    {
        foreach (var (uid, address) in _addresses.ToArray())
        {
            if (address.Rule == args.Rule && TryComp<SWGlobalWarPlanetComponent>(args.Planet, out var planet) &&
                address.Sector == planet.Prototype)
                Unregister(uid);
        }
    }

    private void Unregister(EntityUid uid)
    {
        if (!_addresses.Remove(uid, out var address) || !_anchors.TryGetValue(address, out var entries))
            return;
        entries.Remove(uid);
        if (entries.Count == 0)
            _anchors.Remove(address);
    }

    /// <summary>Reads an explicitly configured named link without resolving or loading its destination.</summary>
    public bool TryGetLink(EntityUid uid, string name, [NotNullWhen(true)] out SWGlobalWarAnchorRef? reference)
    {
        reference = null;
        return TryComp<SWGlobalWarMapAnchorComponent>(uid, out var anchor) && anchor.Links.TryGetValue(name, out reference);
    }

    /// <summary>Resolves exactly one surviving object in the active round; missing/ambiguous addresses fail closed.</summary>
    public bool TryResolveAnchor(SWGlobalWarAnchorRef reference, [NotNullWhen(true)] out EntityUid? uid)
    {
        uid = null;
        if (!_rules.TryGetGlobalWarGamerule(out var rule) ||
            !_anchors.TryGetValue((rule.Owner, reference.Sector, reference.Location, reference.AnchorId), out var entries))
            return false;
        foreach (var entry in entries.ToArray())
        {
            if (TerminatingOrDeleted(entry) || !TryComp<SWGlobalWarMapAnchorComponent>(entry, out var registered) ||
                Transform(entry).MapUid != registered.RegisteredMap ||
                !TryComp<SWGlobalWarPlanetMapComponent>(registered.RegisteredMap, out var registeredMap) ||
                !TryComp<SWGlobalWarPlanetComponent>(registeredMap.Planet, out var registeredPlanet) ||
                registeredPlanet.GlobalWarGamerule != rule.Owner)
                Unregister(entry);
        }
        if (entries.Count != 1)
            return false;
        var candidate = entries.Single();
        if (TerminatingOrDeleted(candidate) || !TryComp<SWGlobalWarMapAnchorComponent>(candidate, out var anchor) ||
            Transform(candidate).MapUid != anchor.RegisteredMap ||
            !TryComp<SWGlobalWarPlanetMapComponent>(anchor.RegisteredMap, out var map) ||
            !TryComp<SWGlobalWarPlanetComponent>(map.Planet, out var planet) || planet.GlobalWarGamerule != rule.Owner)
            return false;
        uid = candidate;
        return true;
    }

    private void Clear()
    {
        _anchors.Clear();
        _addresses.Clear();
    }
}
