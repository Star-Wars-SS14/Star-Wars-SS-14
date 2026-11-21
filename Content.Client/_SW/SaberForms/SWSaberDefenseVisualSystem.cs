using Content.Shared._SW.SaberForms;
using Robust.Client.Graphics;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Client._SW.SaberForms;

public sealed class SWSaberDefenseVisualSystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlays = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private SWSaberDefenseOverlay _overlay = default!;

    public override void Initialize()
    {
        base.Initialize();
        _overlay = new SWSaberDefenseOverlay(EntityManager, _transform, _timing);
        _overlays.AddOverlay(_overlay);
        SubscribeNetworkEvent<SWSaberDefenseEffectEvent>(OnDefenseEffect);
    }

    public override void Shutdown()
    {
        _overlays.RemoveOverlay<SWSaberDefenseOverlay>();
        base.Shutdown();
    }

    private void OnDefenseEffect(SWSaberDefenseEffectEvent effect)
    {
        if (!TryGetEntity(effect.Coordinates.NetEntity, out var anchor))
            return;

        var contact = new EntityCoordinates(anchor.Value, effect.Coordinates.Position);
        _overlay.Add(contact, effect.Visual, effect.Direction, effect.SourceDirection);
    }
}
