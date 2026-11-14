using Robust.Client.Graphics;
using Robust.Shared.Enums;      
using Robust.Shared.Maths;
using System.Numerics;

namespace Content.Client._SW.Overlays;

public sealed class SWActiveAbilityIndicatorOverlay : Overlay
{
   
    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    private readonly Texture _icon;
    private Vector2 _position;
    private readonly float _size;

    public Texture Icon => _icon;

    public SWActiveAbilityIndicatorOverlay(Texture icon, Vector2 position, float size)
    {
        _icon = icon;
        _position = position;
        _size = size;
    }

    public void UpdatePosition(Vector2 newPosition)
    {
        _position = newPosition;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.ScreenHandle;
        
        var iconSize = new Vector2(_size, _size);
        
        var drawBox = UIBox2.FromDimensions(_position - iconSize * 0.5f, iconSize);
        
        handle.DrawTextureRect(_icon, drawBox, Color.White);
    }
}