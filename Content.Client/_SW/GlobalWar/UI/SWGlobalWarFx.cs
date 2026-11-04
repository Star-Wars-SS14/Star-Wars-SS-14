using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;

namespace Content.Client._SW.GlobalWar.UI;

/// <summary>
/// One pixelfx sheet, kept as a plain PNG because the RSI schema has no MIT licence: see attributions.yml beside them.
/// A frame is picked by time and drawn around the effect's own pivot.
/// </summary>
public sealed class SWGlobalWarFx(string file, Vector2i frameSize, int columns, int frames, float fps, Vector2 pivot)
{
    public static readonly SWGlobalWarFx SunburstRepublic = new("sunburst_republic", new Vector2i(128, 128), 8, 60, 15, new Vector2(64, 64));
    public static readonly SWGlobalWarFx SunburstEmpire = new("sunburst_empire", new Vector2i(128, 128), 8, 60, 15, new Vector2(64, 64));
    public static readonly SWGlobalWarFx ZapContested = new("zap_contested", new Vector2i(144, 112), 5, 28, 30, new Vector2(76, 56));
    public static readonly SWGlobalWarFx BurstRepublic = new("burst_republic", new Vector2i(224, 200), 7, 42, 30, new Vector2(112, 100));
    public static readonly SWGlobalWarFx BurstEmpire = new("burst_empire", new Vector2i(224, 200), 7, 42, 30, new Vector2(112, 100));

    private Texture? _sheet;

    public float Duration => frames / fps;

    /// <summary>Draws the frame at this time. Time past the end draws nothing unless the effect loops.</summary>
    public void Draw(DrawingHandleScreen handle, Vector2 at, float scale, float time, bool loop = false, float alpha = 1)
    {
        var frame = (int) (time * fps);
        if (loop)
            frame %= frames;
        if (frame < 0 || frame >= frames)
            return;

        _sheet ??= IoCManager.Resolve<IResourceCache>()
            .GetResource<TextureResource>($"/Textures/_SW/Interface/MegaMode/fx/{file}.png").Texture;
        var corner = new Vector2(frame % columns * frameSize.X, frame / columns * frameSize.Y);
        var topLeft = at - pivot * scale;
        handle.DrawTextureRectRegion(_sheet, UIBox2.FromDimensions(topLeft, (Vector2) frameSize * scale),
            UIBox2.FromDimensions(corner, frameSize), Color.White.WithAlpha(alpha));
    }
}
