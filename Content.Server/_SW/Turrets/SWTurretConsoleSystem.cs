using Content.Shared._SW.TaskSystem;
using Content.Shared._SW;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Robust.Server.GameObjects;
using Content.Shared.Roles;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.Xenoarchaeology.Equipment.Components;
using Content.Server._SW.TaskSystem;
using Content.Server._SW.TaskSystem.TargetSelector;
using Content.Shared.Popups;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Server.Popups;
using Content.Server.DeviceLinking.Systems;
using Content.Shared.DeviceLinking;
using Content.Server._SW.Droids;

namespace Content.Server._SW.TurretConsole;

public sealed partial class SWTurretConsoleSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly UserInterfaceSystem _uiSystem = default!;
    [Dependency] private readonly SWFactionGameruleSystem _sWFaction = default!;
    [Dependency] private readonly AccessReaderSystem _accessReaderSystem = default!;
    [Dependency] private readonly PopupSystem _popupSystem = default!;
    [Dependency] private readonly DeviceLinkSystem _signalSystem = default!;
    [Dependency] private readonly SWDroidTargetingTabletSystem _droidTargetingTablet = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWTurretConsoleComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<SWTurretConsoleComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<SWTurretConsoleComponent, SWTurretJobToggleMessage>(OnToggle);
        SubscribeLocalEvent<SWTurretConsoleComponent, SWAllowFaction>(OnAllow);
        SubscribeLocalEvent<SWTurretConsoleComponent, SWDisallowFaction>(OnDisAllow);
        SubscribeLocalEvent<SWTurretConsoleComponent, NewLinkEvent>(OnNewLink);
        SubscribeLocalEvent<SWTurretConfigComponent, PortDisconnectedEvent>(OnPortDisconnected);
    }

    private void OnMapInit(Entity<SWTurretConsoleComponent> ent, ref MapInitEvent args)
    {
        var comp = Comp<DeviceLinkSinkComponent>(ent.Owner);
        foreach (var source in comp.LinkedSources)
        {
            EnsureTurretConfig(ent, source);
        }
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
        _droidTargetingTablet.ApplyConfigFromTablet(ent.Owner, ent.Comp, user);
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
        _droidTargetingTablet.ApplyConfigFromTablet(ent.Owner, ent.Comp, user);
    }

    private void OnPortDisconnected(Entity<SWTurretConfigComponent> ent, ref PortDisconnectedEvent args)
    {
        //if port is disconnected remove config then
        RemoveTurretConfig(ent.Owner);
    }

    private void OnNewLink(Entity<SWTurretConsoleComponent> ent, ref NewLinkEvent args)
    {
        EnsureTurretConfig(ent, args.Source);
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
        _droidTargetingTablet.ApplyConfigFromTablet(ent.Owner, ent.Comp, user);
    }

    private void OnStartup(Entity<SWTurretConsoleComponent> ent, ref ComponentStartup args)
    {
        var defaultAllowed = HasComp<SWDroidTargetingTabletComponent>(ent.Owner);

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

    private void EnsureTurretConfig(Entity<SWTurretConsoleComponent> console, EntityUid turret)
    {
        var comp = EnsureComp<SWTurretConfigComponent>(turret);
        comp.AllowedJobs = console.Comp.JobsAllowed;
        comp.TargetUnconfiguredJobs = true;
    }

    private void RemoveTurretConfig(EntityUid turret)
    {
        RemComp<SWTurretConfigComponent>(turret);
    }
}
