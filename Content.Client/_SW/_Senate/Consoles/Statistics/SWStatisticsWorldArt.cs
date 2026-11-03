using System.Collections.Generic;
using Content.Shared._SW._Senate.Sectors;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.IoC;

namespace Content.Client._SW._Senate.Consoles.Statistics;

/// <summary>Decorative illustrations; the chosen image does not describe an unknown world's biome.</summary>
public static class SWStatisticsWorldArt
{
    private static readonly string[] Variants = { "continental", "archipelago", "arid", "volcanic", "ice", "gas" };
    private static readonly Dictionary<string, Texture?> Textures = new();

    public static Texture? Get(SWGalacticMapWorld? world, bool detail)
    {
        if (world == null || world.Kind is "Орбитальная станция" or "Верфи") return null;
        string variant;
        if (world.Id == "dromund_kalakar") variant = "gas";
        else
        {
            // Stable across processes and platforms, independent of randomised string hashes.
            uint hash = 2166136261;
            foreach (var ch in world.Id) hash = unchecked((hash ^ ch) * 16777619);
            variant = Variants[(int) (hash % (uint) Variants.Length)];
        }
        var key = $"{variant}-{(detail ? 64 : 32)}";
        if (Textures.TryGetValue(key, out var cached)) return cached;
        var path = $"/Textures/_SW/Interface/Senate/Statistics/Worlds/planet-{key}.png";
        var cache = IoCManager.Resolve<IResourceCache>();
        var result = cache.TryGetResource<TextureResource>(path, out var resource) ? resource.Texture : null;
        Textures[key] = result;
        return result;
    }
}
