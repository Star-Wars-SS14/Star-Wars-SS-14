using Content.Shared._SW.ModularSaber;
using Content.Shared.Toggleable;
using Robust.Client.GameObjects;

namespace Content.Client._SW.ModularSaber;

[RegisterComponent]
public sealed partial class SWDoubleSaberThrowVisualComponent : Component { }

public sealed class SWDoubleSaberThrowVisualSystem : EntitySystem
{
    [Dependency] private readonly AppearanceSystem _appearance = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWDoubleSaberThrowVisualComponent, AppearanceChangeEvent>(OnAppearanceChange);
    }

    private void OnAppearanceChange(EntityUid uid, SWDoubleSaberThrowVisualComponent comp, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null || !_sprite.LayerMapTryGet((uid, args.Sprite), "thrown-blade", out var layer, false))
            return;

        var flying = _appearance.TryGetData<bool>(uid, SWDoubleSaberThrowVisuals.Flying, out var value, args.Component) && value;
        _sprite.LayerSetVisible((uid, args.Sprite), layer, flying);
        if (_appearance.TryGetData<Color>(uid, ToggleableVisuals.Color, out var color, args.Component))
            _sprite.LayerSetColor((uid, args.Sprite), layer, color);
    }
}
