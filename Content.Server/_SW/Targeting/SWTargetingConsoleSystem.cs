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

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWTurretConsoleComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<SWTurretConsoleComponent, SWTurretJobToggleMessage>(OnToggle);
        SubscribeLocalEvent<SWTurretConsoleComponent, SWAllowFaction>(OnAllow);
        SubscribeLocalEvent<SWTurretConsoleComponent, SWDisallowFaction>(OnDisAllow);
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
        ent.Comp.JobsAllowed[args.Job] = !ent.Comp.JobsAllowed[args.Job];
        UpdateInterface(ent.Owner, ent.Comp);
        RaiseSettingsChanged(ent.Owner, user);
    }

    private void OnStartup(Entity<SWTurretConsoleComponent> ent, ref ComponentStartup args)
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
        var state = new SWTurretConsoleBounderUserInterfaceState(comp.JobsAllowed, res);
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

    private void RaiseSettingsChanged(EntityUid uid, EntityUid user)
    {
        RaiseLocalEvent(uid, new SWTargetingConsoleSettingsChangedEvent(user));
    }
}
