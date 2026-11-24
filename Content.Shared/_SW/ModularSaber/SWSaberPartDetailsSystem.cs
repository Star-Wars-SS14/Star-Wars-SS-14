using System.Linq;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Examine;
using Robust.Shared.Prototypes;

namespace Content.Shared._SW.ModularSaber;

/// <summary>Describes the same modular saber parts from prototypes in loadouts and from entities on examine.</summary>
public sealed class SWSaberPartDetailsSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypes = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SWSaberEmitterComponent, ExaminedEvent>(OnPartExamined<SWSaberEmitterComponent>);
        SubscribeLocalEvent<SWSaberFocusComponent, ExaminedEvent>(OnPartExamined<SWSaberFocusComponent>);
        SubscribeLocalEvent<SWSaberCrystalComponent, ExaminedEvent>(OnPartExamined<SWSaberCrystalComponent>);
    }

    public string? GetPrototypeDetails(EntityPrototype prototype)
    {
        var factory = EntityManager.ComponentFactory;
        prototype.TryGetComponent<SWSaberEmitterComponent>(out var emitter, factory);
        prototype.TryGetComponent<SWSaberFocusComponent>(out var focus, factory);
        prototype.TryGetComponent<SWSaberCrystalComponent>(out var crystal, factory);

        return FormatDetails(SelectPart(emitter, focus, crystal));
    }

    private void OnPartExamined<T>(Entity<T> entity, ref ExaminedEvent args) where T : Component
    {
        if (!args.IsInDetailsRange)
            return;

        TryComp<SWSaberEmitterComponent>(entity.Owner, out var emitter);
        TryComp<SWSaberFocusComponent>(entity.Owner, out var focus);
        TryComp<SWSaberCrystalComponent>(entity.Owner, out var crystal);

        // Only the chosen part handler emits text if a prototype has more than one part component.
        var part = SelectPart(emitter, focus, crystal);
        if (ReferenceEquals(entity.Comp, part) && FormatDetails(part) is { } details)
            args.PushText(details);
    }

    private static Component? SelectPart(SWSaberEmitterComponent? emitter, SWSaberFocusComponent? focus,
        SWSaberCrystalComponent? crystal)
    {
        if (emitter != null && !emitter.Damage.Empty)
            return emitter;

        return (Component?) focus ?? crystal;
    }

    private string? FormatDetails(Component? part)
    {
        return part switch
        {
            SWSaberEmitterComponent emitter => FormatEmitter(emitter),
            SWSaberFocusComponent focus => Loc.GetString("sw-loadout-saber-focus-stats",
                ("damage", MathF.Round(focus.DamageMultiplier * 100f, 2)),
                ("rate", MathF.Round(focus.AttackRateMultiplier * 100f, 2))),
            SWSaberCrystalComponent => Loc.GetString("sw-loadout-saber-crystal-color"),
            _ => null,
        };
    }

    private string FormatEmitter(SWSaberEmitterComponent emitter)
    {
        var values = emitter.Damage.DamageDict.Select(damage =>
        {
            var name = _prototypes.TryIndex<DamageTypePrototype>(damage.Key, out var type)
                ? type.LocalizedName
                : damage.Key;
            return Loc.GetString("sw-loadout-saber-emitter-damage-value",
                ("type", name), ("amount", damage.Value.Double()));
        });
        return Loc.GetString("sw-loadout-saber-emitter-damage", ("damage", string.Join(", ", values)));
    }
}
