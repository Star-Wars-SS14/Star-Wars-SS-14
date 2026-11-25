using Content.Server._SW.Cultist;
using Content.Shared._SW.TaskSystem;

namespace Content.Server._SW.TaskSystem;

[RegisterComponent]
public sealed partial class SWEliminateCultTaskComponent : Component;

// Its presence caches the final result in SWTaskComponent; Scored makes credit idempotent.
[RegisterComponent]
public sealed partial class SWFinalTaskStateComponent : Component
{
    public bool Scored;
}

public sealed class SWEliminateCultTaskSystem : EntitySystem
{
    [Dependency] private readonly SWCultistGameRuleSystem _cult = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWEliminateCultTaskComponent, SWGetProgressEvent>(OnProgress);
    }

    private void OnProgress(Entity<SWEliminateCultTaskComponent> ent, ref SWGetProgressEvent args)
    {
        args.Progress = _cult.AllAssignedCultistsDead() ? 1 : 0;
        args.Handled = true;
    }
}
