using Content.Shared.Interaction;
using Content.Shared.Light;
using Content.Shared.Light.Components;
using Content.Shared.Toggleable;
using Content.Shared.Tools.Components;
using Content.Shared.Item;
using Robust.Shared.Random;

namespace Content.Server.Weapons.Melee.EnergySword;

public sealed class EnergySwordSystem : EntitySystem
{
    [Dependency] private readonly SharedRgbLightControllerSystem _rgbSystem = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<EnergySwordComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<EnergySwordComponent, InteractUsingEvent>(OnInteractUsing);
    }
    // Used to pick a colors from list for the blade on map init.
    private void OnMapInit(EntityUid uid, EnergySwordComponent comp, MapInitEvent args)
    {
        if (comp.ColorOptions.Count != 0)
            CheckColor(uid, comp);

        if (!TryComp(uid, out AppearanceComponent? appearanceComponent))
            return;
        _appearance.SetData(uid, ToggleableLightVisuals.Color, comp.ActivatedColor, appearanceComponent);
    }

    private void CheckColor(EntityUid uid, EnergySwordComponent comp)
    {
        Dictionary<string, int> colorMapping = new Dictionary<string, int>
        {
            { "Red", 0 },
            { "Blue", 1 },
            { "Green", 2 },
            { "Purple", 3 }
        };

        if (colorMapping.TryGetValue(comp.ColorBlade, out int colorIndex))
        {
            comp.ActivatedColor = comp.ColorOptions[colorIndex];
        }
    }


    // Used to make the make the blade multicolored when using a multitool on it.
    private void OnInteractUsing(EntityUid uid, EnergySwordComponent comp, InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (!TryComp(args.Used, out ToolComponent? tool) || !tool.Qualities.ContainsAny("Pulsing"))
            return;

        args.Handled = true;
        comp.Hacked = !comp.Hacked;

        if (comp.Hacked)
        {
            var rgb = EnsureComp<RgbLightControllerComponent>(uid);
            _rgbSystem.SetCycleRate(uid, comp.CycleRate, rgb);
        }
        else
            RemComp<RgbLightControllerComponent>(uid);
    }
}
