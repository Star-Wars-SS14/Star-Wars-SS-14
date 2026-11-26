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
        ["ilum"] = "ice",
        ["dantooine"] = "continental",
        ["ord_mantell"] = "archipelago",
        ["taris"] = "neutral-obscured",
        ["korriban"] = "arid",
        ["ziost"] = "ice",
        ["begeren"] = "neutral-obscured",
        ["ashas_ree"] = "continental",
        ["athiss"] = "neutral-obscured",
        ["dromund_kaas"] = "continental",
        ["dromund_ixin"] = "neutral-obscured",
        ["dromund_kalakar"] = "gas",
        ["dromund_fels"] = "arid",
        ["dromund_tyne"] = "neutral-obscured",
        ["voss"] = "neutral-obscured",
        ["coruscant"] = "urban-coruscant",
        ["brentaal"] = "arid",
        ["tython"] = "continental",
        ["alderaan"] = "continental",
        ["kuat"] = "continental",
        ["balmorra"] = "neutral-obscured",
        ["corellia"] = "continental",
        ["onderon"] = "neutral-obscured",
        ["kashyyyk"] = "forest-kashyyyk",
        ["quesh"] = "toxic-swamp-quesh",
        ["nal_hutta"] = "swamp-hutta",
        ["bothawui"] = "continental",
        ["tatooine"] = "arid",
        ["ryloth"] = "arid",
        ["abregado_rae"] = "neutral-obscured",
        ["thyferra"] = "neutral-obscured",
        ["sullust"] = "volcanic",
        ["eriadu"] = "neutral-obscured",
        ["hoth"] = "ice",
        ["belsavis"] = "ice-rifts-belsavis",
        ["chandrila"] = "neutral-obscured",
        ["denon"] = "neutral-obscured",
        ["fondor"] = "neutral-obscured",
        ["anaxes"] = "neutral-obscured",
        ["telos"] = "neutral-obscured",
        ["dac"] = "archipelago",
        ["ithor"] = "continental",
        ["bonadan"] = "neutral-obscured",
        ["ruusan"] = "neutral-obscured",
        ["duro"] = "neutral-obscured",
        ["rendili"] = "neutral-obscured",
        ["corulag"] = "neutral-obscured",
        ["ralltiir"] = "neutral-obscured",
        ["carida"] = "neutral-obscured",
        ["contruum"] = "neutral-obscured",
        ["yag_dhul"] = "neutral-obscured",
        ["duneeden"] = "neutral-obscured",
        ["utrost"] = "neutral-obscured",
        ["quenk"] = "neutral-obscured",
        ["ashla"] = "neutral-obscured",
        ["bogan"] = "neutral-obscured",
        ["delaya"] = "neutral-obscured",
        ["selonia"] = "neutral-obscured",
        ["drall"] = "neutral-obscured",
        ["talus"] = "neutral-obscured",
        ["tralus"] = "continental",
        ["monadin"] = "neutral-obscured",
        ["bormea"] = "neutral-obscured",
        ["nopces"] = "neutral-obscured",
        ["iseno"] = "neutral-obscured",
        ["kommendor"] = "neutral-obscured",
        ["procopia"] = "archipelago",
        ["achillea"] = "neutral-obscured",
        ["aleron"] = "neutral-obscured",
        ["anthus"] = "neutral-obscured",
        ["agomar"] = "neutral-obscured",
        ["belloria"] = "neutral-obscured",
        ["adras"] = "neutral-obscured",
        ["tavris"] = "neutral-obscured",
        ["artus_prime"] = "neutral-obscured",
        ["kestos_minor"] = "neutral-obscured",
        ["anobis"] = "continental",
        ["aleen"] = "neutral-obscured",
        ["bestin"] = "neutral-obscured",
        ["gravlex_med"] = "continental",
        ["alurion"] = "neutral-obscured",
        ["pammant"] = "neutral-obscured",
        ["pinperu"] = "neutral-obscured",
        ["kothlis"] = "archipelago",
        ["krant"] = "neutral-obscured",
        ["mytaranor"] = "neutral-obscured",
        ["randon"] = "neutral-obscured",
        ["arthon"] = "neutral-obscured",
        ["orocco"] = "neutral-obscured",
        ["garnib"] = "neutral-obscured",
        ["brodogon"] = "neutral-obscured",
        ["belkadan"] = "continental",
        ["sernpidal"] = "neutral-obscured",
        ["ruuria"] = "continental",
        ["nez_peron"] = "neutral-obscured",
        ["taral_v"] = "continental",
    };
    private static readonly Dictionary<string, Texture?> Textures = new();

    public static Texture? Get(SWGalacticMapWorld? world, bool detail)
    {
        if (world == null || world.Kind is "Орбитальная станция" or "Верфи") return null;
        if (!WorldVariants.TryGetValue(world.Id, out var variant)) variant = "neutral-obscured";
        var key = $"{variant}-{(detail ? 64 : 32)}";
        if (Textures.TryGetValue(key, out var cached)) return cached;
        var path = $"/Textures/_SW/Interface/Senate/Statistics/Worlds/planet-{key}.png";
        var cache = IoCManager.Resolve<IResourceCache>();
        var result = cache.TryGetResource<TextureResource>(path, out var resource) ? resource.Texture : null;
        Textures[key] = result;
        return result;
    }

    public static bool CanDraw(SWGalacticMapWorld? world, bool detail = true) =>
        world != null && (world.Kind is "Орбитальная станция" or "Верфи" || Get(world, detail) != null);

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


    }
}
