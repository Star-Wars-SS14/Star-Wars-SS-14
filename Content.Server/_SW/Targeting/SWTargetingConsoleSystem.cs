using Content.Shared._SW;
using Content.Server._SW.TaskSystem;
using Content.Server._SW.TurretConsole;
using Robust.Server.GameObjects;
using Content.Shared.Popups;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Server.Popups;

namespace Content.Server._SW.Targeting;

public sealed partial class SWTargetingConsoleSystem : EntitySystem
{
    [Dependency] private readonly UserInterfaceSystem _uiSystem = default!;
    [Dependency] private readonly SWFactionGameruleSystem _sWFaction = default!;
    [Dependency] private readonly AccessReaderSystem _accessReaderSystem = default!;
    [Dependency] private readonly PopupSystem _popupSystem = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWTurretConsoleComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<SWTurretConsoleComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<SWTurretConsoleComponent, SWTurretJobToggleMessage>(OnToggle);
        SubscribeLocalEvent<SWTurretConsoleComponent, SWAllowFaction>(OnAllow);
        SubscribeLocalEvent<SWTurretConsoleComponent, SWDisallowFaction>(OnDisAllow);
        SubscribeLocalEvent<SWTurretConsoleComponent, SWToggleTargetHumanoidsWithoutIdMessage>(OnToggleTargetHumanoidsWithoutId);
        SubscribeLocalEvent<SWTurretConsoleComponent, SWToggleTargetAnimalsMessage>(OnToggleTargetAnimals);
    }

    private void OnDisAllow(Entity<SWTurretConsoleComponent> ent, ref SWDisallowFaction args)
    {
        if (args.Actor is not { Valid: true } user)
            return;

        if (!CanUse(user, ent.Owner))
        {
            _popupSystem.PopupCursor(Loc.GetString("comms-console-permission-denied"), user, PopupType.Medium);
            return;
        }

        if (!IsProviderEnabled(user, ent.Owner))
            return;

        var faction = _sWFaction.GetFactionEntity(args.Faction);
        if (faction == null)
        {
            return;
        }

        foreach (var job in faction.Value.Comp.RelatedJobs)
        {
            if (ent.Comp.JobsAllowed.ContainsKey(job))
            {
                ent.Comp.JobsAllowed[job] = false;
            }
        }
        UpdateInterface(ent.Owner, ent.Comp);
        RaiseSettingsChanged(ent.Owner, user);
    }

    private void OnAllow(Entity<SWTurretConsoleComponent> ent, ref SWAllowFaction args)
    {
        if (args.Actor is not { Valid: true } user)
            return;

        if (!CanUse(user, ent.Owner))
        {
            _popupSystem.PopupCursor(Loc.GetString("comms-console-permission-denied"), user, PopupType.Medium);
            return;
        }

        if (!IsProviderEnabled(user, ent.Owner))
            return;

        var faction = _sWFaction.GetFactionEntity(args.Faction);
        if (faction == null)
        {
            return;
        }

        foreach (var job in faction.Value.Comp.RelatedJobs)
        {
            if (ent.Comp.JobsAllowed.ContainsKey(job))
            {
                ent.Comp.JobsAllowed[job] = true;
            }
        }
        UpdateInterface(ent.Owner, ent.Comp);
        RaiseSettingsChanged(ent.Owner, user);
    }

    private void OnToggle(Entity<SWTurretConsoleComponent> ent, ref SWTurretJobToggleMessage args)
    {
        if (args.Actor is not { Valid: true } user)
            return;

        if (!CanUse(user, ent.Owner))
        {
            _popupSystem.PopupCursor(Loc.GetString("comms-console-permission-denied"), user, PopupType.Medium);
            return;
        }

        if (!IsProviderEnabled(user, ent.Owner))
            return;

        if (!ent.Comp.JobsAllowed.TryGetValue(args.Job, out var allowed))
            return;

        ent.Comp.JobsAllowed[args.Job] = !allowed;
        UpdateInterface(ent.Owner, ent.Comp);
        RaiseSettingsChanged(ent.Owner, user);
    }

    private void OnToggleTargetHumanoidsWithoutId(Entity<SWTurretConsoleComponent> ent, ref SWToggleTargetHumanoidsWithoutIdMessage args)
    {
        if (args.Actor is not { Valid: true } user)
            return;

        if (!CanUse(user, ent.Owner))
        {
            _popupSystem.PopupCursor(Loc.GetString("comms-console-permission-denied"), user, PopupType.Medium);
            return;
        }

        if (!IsProviderEnabled(user, ent.Owner))
            return;

        var targets = EnsureComp<SWTargetingConfigTargetsComponent>(ent.Owner);
        targets.TargetHumanoidsWithoutId = !targets.TargetHumanoidsWithoutId;
        UpdateInterface(ent.Owner, ent.Comp);
        RaiseSettingsChanged(ent.Owner, user);
    }

    private void OnToggleTargetAnimals(Entity<SWTurretConsoleComponent> ent, ref SWToggleTargetAnimalsMessage args)
    {
        if (args.Actor is not { Valid: true } user)
            return;

        if (!CanUse(user, ent.Owner))
        {
            _popupSystem.PopupCursor(Loc.GetString("comms-console-permission-denied"), user, PopupType.Medium);
            return;
        }

        if (!IsProviderEnabled(user, ent.Owner))
            return;

        var targets = EnsureComp<SWTargetingConfigTargetsComponent>(ent.Owner);
        targets.TargetAnimals = !targets.TargetAnimals;
        UpdateInterface(ent.Owner, ent.Comp);
        RaiseSettingsChanged(ent.Owner, user);
    }

    /// <summary>
    /// Fills the job list when the console's map is initialized, so an uninitialized console in a map file saves as
    /// its prototype. Consoles on a live map, or loaded from an initialized save, still get it on startup.
    /// </summary>
    private void OnStartup(Entity<SWTurretConsoleComponent> ent, ref ComponentStartup args)
    {
        if (_map.IsInitialized(Transform(ent).MapUid))
            FillJobs(ent);
    }

    private void OnMapInit(Entity<SWTurretConsoleComponent> ent, ref MapInitEvent args)
    {
        FillJobs(ent);
    }

    private void FillJobs(Entity<SWTurretConsoleComponent> ent)
    {
        var defaultAllowed = TryComp<SWTargetingConfigTargetsComponent>(ent.Owner, out var targets) &&
                             targets.DefaultAllowedJobs;

        foreach (var job in ent.Comp.Jobs)
        {
            if (ent.Comp.JobsAllowed.ContainsKey(job))
            {
                continue;
            }
            ent.Comp.JobsAllowed[job] = defaultAllowed;
        }

        UpdateInterface(ent.Owner, ent.Comp);
    }

    private void UpdateInterface(EntityUid uid, SWTurretConsoleComponent comp)
    {
        var factions = _sWFaction.GetFactions();
        Dictionary<string, string> res = new Dictionary<string, string>();
        foreach (var (id, ent) in factions)
        {
            if (ent == null)
            {
                continue;
            }
            res[id] = ent.Value.Comp.Name;
        }
        TryComp<SWTargetingConfigTargetsComponent>(uid, out var targets);
        var state = new SWTurretConsoleBounderUserInterfaceState(
            comp.JobsAllowed,
            res,
            targets?.TargetHumanoidsWithoutId ?? false,
            targets?.TargetAnimals ?? false);
        _uiSystem.SetUiState(uid, SWTurretConsoleUiKey.Key, state);
    }
    private bool CanUse(EntityUid user, EntityUid console)
    {
        if (TryComp<AccessReaderComponent>(console, out var accessReaderComponent))
        {
            return _accessReaderSystem.IsAllowed(user, console, accessReaderComponent);
        }
        return true;
    }

    private bool IsProviderEnabled(EntityUid user, EntityUid provider)
    {
        var ev = new SWTargetingProviderAvailableEvent(user, true);
        RaiseLocalEvent(provider, ev);
        return !ev.Cancelled;
    }

    private void RaiseSettingsChanged(EntityUid uid, EntityUid user)
    {
        RaiseLocalEvent(uid, new SWTargetingConsoleSettingsChangedEvent(user));
    }
}
