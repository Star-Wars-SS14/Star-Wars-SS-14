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

namespace Content.Server._SW.TurretConsole;

public sealed partial class SWTurretConsoleSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly UserInterfaceSystem _uiSystem = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWTurretConsoleComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<SWTurretConsoleComponent, SWTurretJobToggleMessage>(OnToggle);
    }

    private void OnToggle(Entity<SWTurretConsoleComponent> ent, ref SWTurretJobToggleMessage args)
    {
        ent.Comp.JobsAllowed[args.Job] = !ent.Comp.JobsAllowed[args.Job];
        UpdateInterface(ent.Owner, ent.Comp);
    }

    private void OnStartup(Entity<SWTurretConsoleComponent> ent, ref ComponentStartup args)
    {
        var jobList = _prototype.EnumeratePrototypes<JobPrototype>().ToList();

        foreach (var job in jobList)
        {
            ent.Comp.JobsAllowed[job.ID] = false;
        }

        foreach (var job in ent.Comp.StartJobsAllowed)
        {
            ent.Comp.JobsAllowed[job] = true;
        }

        UpdateInterface(ent.Owner, ent.Comp);
    }

    private void UpdateInterface(EntityUid uid, SWTurretConsoleComponent comp)
    {
        var state = new SWTurretConsoleBounderUserInterfaceState(comp.JobsAllowed);
        _uiSystem.SetUiState(uid, SWTurretConsoleUiKey.Key, state);
    }
}
