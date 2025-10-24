using Content.Server._SW.TaskSystem.ParrallelTask;
using Content.Shared._SW.TaskSystem;


namespace Content.Server._SW.TaskSystem.ParallelTask;

public sealed partial class SWParallelTaskSystem : EntitySystem
{
    [Dependency] private readonly SWTasksGameRuleSystem _sWTasks = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWParallelTaskComponent, SWTaskCheckRequirementsEvent>(OnCheckRequirements);
        SubscribeLocalEvent<SWParallelTaskComponent, SWTaskInitEvent>(OnTaskInit);
    }
    private void OnCheckRequirements(Entity<SWParallelTaskComponent> ent, ref SWTaskCheckRequirementsEvent args)
    {
        if (!_sWTasks.CheckTask(ent.Comp.Task, ent.Comp.Faction))
        {
            args.Cancel();
        }
    }
    private void OnTaskInit(Entity<SWParallelTaskComponent> ent, ref SWTaskInitEvent args)
    {
        var spawnedTask = _sWTasks.AddTaskNoInit(ent.Comp.Faction, ent.Comp.Task);
        RaiseLocalEvent(ent.Owner, new AfterParallelTaskSpawnedEvent(spawnedTask));
        _sWTasks.InitTask(spawnedTask);
    }
}

public sealed class AfterParallelTaskSpawnedEvent : EntityEventArgs
{
    public Entity<SWTaskComponent> SpawnedTask;

    public AfterParallelTaskSpawnedEvent(Entity<SWTaskComponent> task)
    {
        SpawnedTask = task;
    }
}
