using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;
using System;

namespace Content.Server._SW;

public sealed class LightningSparksSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    
    private const float SparkSpawnInterval = 0.2f; // Спавн искр каждые 0.2 секунды
    
    public void AddLightningSparks(EntityUid target, float duration)
    {
        // Remove any old sparks, if present
        if (TryComp<LightningSparksComponent>(target, out var oldComp))
        {
            oldComp.Active = false;
            RemComp<LightningSparksComponent>(target);
        }
        
        // Adding a tracking component
        var comp = EnsureComp<LightningSparksComponent>(target);
        comp.Active = true;
        comp.Timer = 0f;
        
        // We start periodic spawning of sparks
        ScheduleSparks(target, comp, duration);
    }
    
    private void ScheduleSparks(EntityUid target, LightningSparksComponent comp, float remainingTime)
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
public sealed partial class LightningSparksComponent : Component
{
    public bool Active;
    public float Timer;
}