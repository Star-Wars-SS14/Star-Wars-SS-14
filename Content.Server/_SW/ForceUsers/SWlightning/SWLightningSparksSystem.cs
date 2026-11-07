using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;
using System;

namespace Content.Server._SW;

public sealed class SWLightningSparksSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    
    private const float SparkSpawnInterval = 0.2f; 
    
    public void AddLightningSparks(EntityUid target, float duration)
    {
        // Remove any old sparks, if present
        if (TryComp<SWLightningSparksComponent>(target, out var oldComp))
        {
            oldComp.Active = false;
            RemComp<SWLightningSparksComponent>(target);
        }
        
        // Adding a tracking component
        var comp = EnsureComp<SWLightningSparksComponent>(target);
        comp.Active = true;
        comp.Timer = 0f;
        
        // We start periodic spawning of sparks
        ScheduleSparks(target, comp, duration);
    }
    
    private void ScheduleSparks(EntityUid target, SWLightningSparksComponent comp, float remainingTime)
    {
        if (remainingTime <= 0 || !comp.Active)
        {
            comp.Active = false;
            return;
        }
        
        var nextSpawn = Math.Min(SparkSpawnInterval, remainingTime);
        
        Timer.Spawn(TimeSpan.FromSeconds(nextSpawn), () =>
        {
            if (!comp.Active || !target.IsValid())
                return;
            
            // Spawn sparks at the target coordinates
            if (TryComp<TransformComponent>(target, out var transform))
            {
                EntityManager.SpawnEntity("EffectSparks", transform.Coordinates);
            }
            
            // Planning the next spawn
            ScheduleSparks(target, comp, remainingTime - nextSpawn);
        });
    }
}

[RegisterComponent]
public sealed partial class SWLightningSparksComponent : Component
{
    public bool Active;
    public float Timer;
}