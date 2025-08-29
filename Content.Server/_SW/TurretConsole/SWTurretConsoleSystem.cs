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

namespace Content.Server._SW.TurretConsole;

public sealed partial class SWTurretConsoleSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototype = default!;
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
        SubscribeLocalEvent<SWTurretConsoleComponent, NewLinkEvent>(OnNewLink);
        SubscribeLocalEvent<SWTurretConsoleComponent, PortDisconnectedEvent>(OnPortDisconnected);
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
    }

    private void OnPortDisconnected(Entity<SWTurretConsoleComponent> ent, ref PortDisconnectedEvent args)
    {
        //if port is disconnected remove config then
        RemComp<SWTurretConfigComponent>(ent.Owner);
    }

    private void OnNewLink(Entity<SWTurretConsoleComponent> ent, ref NewLinkEvent args)
    {
        var comp = AddComp<SWTurretConfigComponent>(args.Source);
        comp.AllowedJobs = ent.Comp.JobsAllowed;
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
    }

    private void OnStartup(Entity<SWTurretConsoleComponent> ent, ref ComponentStartup args)
    {
        var jobList = _prototype.EnumeratePrototypes<JobPrototype>().ToList();

        foreach (var job in ent.Comp.Jobs)
        {
            if (ent.Comp.JobsAllowed.ContainsKey(job))
            {
                continue;
            }
            ent.Comp.JobsAllowed[job] = false;
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
}
