using System.Collections.Generic;
using System.Numerics;
using Content.Shared._SW._Senate.Sectors;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.IoC;
using Robust.Shared.Maths;

namespace Content.Client._SW._Senate.Consoles.Statistics;

/// <summary>Decorative illustrations; the chosen image does not describe an unknown world's biome.</summary>
public static class SWStatisticsWorldArt
{
    // Only IDs and variants explicitly confirmed for this map belong here.
    private static readonly Dictionary<string, string> WorldVariants = new()
    {
        ["alderaan"] = "continental",
        ["hoth"] = "ice",
        ["korriban"] = "arid",
        ["tatooine"] = "arid",
        ["dromund_kaas"] = "continental",
        ["dac"] = "archipelago",
        ["dromund_kalakar"] = "gas",
    };
    private static readonly Dictionary<string, Texture?> Textures = new();

    public static Texture? Get(SWGalacticMapWorld? world, bool detail)
    {
        if (world == null || world.Kind is "Орбитальная станция" or "Верфи") return null;
        if (!WorldVariants.TryGetValue(world.Id, out var variant)) return null;
        var key = $"{variant}-{(detail ? 64 : 32)}";
        if (Textures.TryGetValue(key, out var cached)) return cached;
        var path = $"/Textures/_SW/Interface/Senate/Statistics/Worlds/planet-{key}.png";
        var cache = IoCManager.Resolve<IResourceCache>();
        var result = cache.TryGetResource<TextureResource>(path, out var resource) ? resource.Texture : null;
        Textures[key] = result;
        return result;
    }

    public static bool CanDraw(SWGalacticMapWorld? world) => world != null;

    private static void DrawInstallation(DrawingHandleScreen handle, UIBox2 bounds, bool shipyard, float alpha)
    {
        var origin = bounds.TopLeft;
        var size = new Vector2(bounds.Width, bounds.Height);
        Vector2 P(float x, float y) => origin + size * new Vector2(x, y);
        var metal = new Color(.30f, .45f, .49f, alpha);
        var edge = new Color(.64f, .79f, .80f, alpha);
        if (shipyard)
        {
            // A pair of docks and a central berth: an installation, not a planet or vessel.
            handle.DrawRect(new UIBox2(P(.16f, .23f), P(.27f, .77f)), metal);
            handle.DrawRect(new UIBox2(P(.73f, .23f), P(.84f, .77f)), metal);
            handle.DrawRect(new UIBox2(P(.27f, .30f), P(.73f, .39f)), metal);
            handle.DrawRect(new UIBox2(P(.27f, .61f), P(.73f, .70f)), metal);
            handle.DrawLine(P(.37f, .5f), P(.63f, .5f), edge);
            handle.DrawLine(P(.5f, .40f), P(.5f, .60f), edge);
        }
        else
        {
            // Hub and solar arrays form a readable station silhouette at both sizes.
            handle.DrawRect(new UIBox2(P(.35f, .35f), P(.65f, .65f)), metal);
            handle.DrawRect(new UIBox2(P(.12f, .28f), P(.27f, .72f)), metal);
            handle.DrawRect(new UIBox2(P(.73f, .28f), P(.88f, .72f)), metal);
            handle.DrawLine(P(.27f, .5f), P(.73f, .5f), edge);
            handle.DrawLine(P(.50f, .20f), P(.50f, .80f), edge);
            handle.DrawRect(new UIBox2(P(.35f, .35f), P(.65f, .65f)), edge, false);
        }
    }

    /// <summary>Draws the same neutral, surface-free silhouette at either display size.</summary>
    public static void Draw(DrawingHandleScreen handle, SWGalacticMapWorld? world, UIBox2 bounds, bool detail, float alpha = 1f)
    {
        if (world == null) return;
        if (world.Kind is "Орбитальная станция" or "Верфи")
        {
            DrawInstallation(handle, bounds, world.Kind == "Верфи", alpha);
            return;
        }
        var texture = Get(world, detail);
        if (texture != null)
        {
            handle.DrawTextureRect(texture, bounds, Color.White.WithAlpha(alpha));
            return;
        }

        var center = (bounds.TopLeft + bounds.BottomRight) / 2;
        var radius = System.MathF.Min(bounds.Width, bounds.Height) * 0.42f;
        handle.DrawCircle(center, radius, Color.ToSrgb(new Color(0.12f, 0.22f, 0.28f, alpha)));
        handle.DrawCircle(center, radius * 0.90f, Color.ToSrgb(new Color(0.32f, 0.47f, 0.51f, alpha)));
        handle.DrawCircle(center, radius, Color.ToSrgb(new Color(0.53f, 0.70f, 0.72f, alpha)), false);
    }
}
