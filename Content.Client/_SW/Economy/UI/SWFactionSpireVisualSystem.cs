using Content.Shared._SW.Economy;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Maths;

namespace Content.Client._SW.Economy;

/// <summary>
/// Applies spire sprite layers and glow from the networked component state.
/// SpriteSystem is client-only in this engine, so the server owns the state and
/// every client renders the layers locally (all players in PVS see glitches/beam).
/// Layers: 0 = tower+antenna, 1 = faction screen, 2 = glitch, 3 = payout beam.
/// </summary>
public sealed class SWFactionSpireVisualSystem : EntitySystem
{
    [Dependency] private readonly SpriteSystem _sprite = default!;
    [Dependency] private readonly SharedPointLightSystem _pointLight = default!;

    private static readonly Dictionary<string, string> FactionSuffix = new()
    {
        { "RepublicFaction", "republic" },
        { "EmpireFaction", "empire" },
        { "MandalorianFaction", "mandalore" },
        { "ExchangeFaction", "exchange" },
        { "BlackSunFaction", "blacksun" },
    };

    private static readonly Dictionary<string, string> FactionColor = new()
    {
        { "RepublicFaction", "#2e6bd6" },
        { "EmpireFaction", "#d63a3a" },
        { "ExchangeFaction", "#8a5a2b" },
        { "BlackSunFaction", "#8a3ad6" },
        { "MandalorianFaction", "#d6c23a" },
    };

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SWFactionSpireComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<SWFactionSpireComponent, AfterAutoHandleStateEvent>(OnAfterState);
    }

    private void OnStartup(Entity<SWFactionSpireComponent> ent, ref ComponentStartup args)
    {
        Apply(ent);
    }

    private void OnAfterState(Entity<SWFactionSpireComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        Apply(ent);
    }

    private void Apply(Entity<SWFactionSpireComponent> ent)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        var comp = ent.Comp;
        var entity = new Entity<SpriteComponent?>(ent.Owner, sprite);

        var ownerSuffix = Suffix(comp.OwnerFaction);
        var glitchSuffix = comp.IsCapturing ? Suffix(comp.CapturingFaction) : ownerSuffix;

        // The relay antenna spins whenever the spire is powered on, i.e. always:
        // even a neutral spire keeps scanning its surroundings.
        _sprite.LayerSetRsiState(entity, 0, "spire_active");

        _sprite.LayerSetRsiState(entity, 1, $"screen_{ownerSuffix}");
        _sprite.LayerSetRsiState(entity, 2, $"glitch_{glitchSuffix}");

        _sprite.LayerSetVisible(entity, 2, comp.IsCapturing);
        _sprite.LayerSetVisible(entity, 3, comp.BeamActive);

        // Glow follows the CAPTURING faction immediately during a rewrite, so a
        // neutral (white) spire tinted by glitches does not keep a white light.
        // SharedPointLightComponent.Color is [Access]-restricted (RA0002 on direct
        // write): mutate it only through the owning system setter.
        if (TryComp<PointLightComponent>(ent, out var light))
        {
            var glowFaction = comp.IsCapturing ? comp.CapturingFaction : comp.OwnerFaction;
            var glowHex = glowFaction == string.Empty
                ? "#ffffff"
                : FactionColor.GetValueOrDefault(glowFaction, "#ffffff");
            _pointLight.SetColor(ent.Owner, Color.FromHex(glowHex), light);
        }
    }

    private static string Suffix(string faction)
    {
        return faction == string.Empty
            ? "none"
            : FactionSuffix.GetValueOrDefault(faction, "none");
    }
}