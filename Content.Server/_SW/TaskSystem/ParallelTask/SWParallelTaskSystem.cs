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
        SubscribeLocalEvent<SWParallelTaskComponent, SWOnTaskInitEvent>(OnTaskInit);
    }
    private void OnCheckRequirements(Entity<SWParallelTaskComponent> ent, ref SWTaskCheckRequirementsEvent args)
    {
        if (!_sWTasks.CheckTask(ent.Comp.Task))
        {
            args.Cancel();
        }
    }
    private void OnTaskInit(Entity<SWParallelTaskComponent> ent, ref SWOnTaskInitEvent args)
    {
        _sWTasks.AddTask(ent.Comp.Faction, ent.Comp.Task);
    }
}
