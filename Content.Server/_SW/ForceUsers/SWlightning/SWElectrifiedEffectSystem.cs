using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;
using System;

namespace Content.Server._SW;

public sealed class SWElectrifiedEffectSystem : EntitySystem
{
    public void AddElectrifiedEffect(EntityUid target, float duration)
    {
        // Remove the old effect, if present
        if (TryComp<SWElectrifiedEffectComponent>(target, out var oldComp) && oldComp.EffectEntity.IsValid())
        {
            EntityManager.DeleteEntity(oldComp.EffectEntity);
            RemComp<SWElectrifiedEffectComponent>(target);
        }

        var transform = EntityManager.GetComponent<TransformComponent>(target);
        var effect = EntityManager.SpawnEntity("EffectElectrified", transform.Coordinates);
        
        // Attach the effect to the target
        var effectTransform = EntityManager.GetComponent<TransformComponent>(effect);
        effectTransform.AttachParent(target);
        
        // Adding a tracking component
        var comp = EnsureComp<SWElectrifiedEffectComponent>(target);
        comp.EffectEntity = effect;
        
        // Remove the effect after a specified time
        Timer.Spawn(TimeSpan.FromSeconds(duration), () =>
        {
            if (TryComp<SWElectrifiedEffectComponent>(target, out var c) && c.EffectEntity.IsValid())
            {
                EntityManager.DeleteEntity(c.EffectEntity);
                RemComp<SWElectrifiedEffectComponent>(target);
            }
        });
    }
}

[RegisterComponent]
public sealed partial class SWElectrifiedEffectComponent : Component
{
    public EntityUid EffectEntity;
}