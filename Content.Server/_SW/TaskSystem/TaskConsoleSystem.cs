using Content.Server.Communications;
using Content.Shared._SW;
using Content.Shared._SW.TaskSystem;
using Content.Shared.Communications;
using Robust.Server.GameObjects;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.TaskSystem;
public sealed partial class TaskConsoleSystem : EntitySystem
{
    [Dependency] private readonly UserInterfaceSystem _uiSystem = default!;
    [Dependency] private readonly SWTasksGameRuleSystem _sWTasks = default!;


    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<TaskConsoleComponent, UpdateTasksMessage>(OnUpdateTasks);
    }

    private void OnUpdateTasks(Entity<TaskConsoleComponent> ent, ref UpdateTasksMessage args)
    {
        UpdateTaskConsoleInterface(ent.Owner, ent.Comp);
    }

    public override void Update(float frameTime)
    {
        //var query = EntityQueryEnumerator<TaskConsoleComponent>();
        //while (query.MoveNext(out var uid, out var comp))
        //{
        //    // TODO refresh the UI in a less horrible way
            
        //    if (_uiSystem.IsUiOpen(uid, TaskConsoleUiKey.Key))
        //        UpdateTaskConsoleInterface(uid, comp);
        //}

        //base.Update(frameTime);
    }

    private void UpdateTaskConsoleInterface(EntityUid uid, TaskConsoleComponent comp)
    {
        List<Entity<SWTaskComponent>> list = _sWTasks.GetFactionTasks(comp.Faction);
        List<TaskInfo> listComp = new();
        foreach(var task in list)
        {
            listComp.Add(new TaskInfo(task.Comp.Name, task.Comp.Description, task.Comp.Completeness));
        }
        var state = new TaskConsoleBounderUserInterfaceState(listComp);

        _uiSystem.SetUiState(uid, TaskConsoleUiKey.Key, state);
    }
}
