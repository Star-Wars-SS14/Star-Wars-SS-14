using Content.Shared._SW.SaberForms;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;

namespace Content.Server._SW.SaberForms;

public sealed class SWSaberFormDefenseVisualSystem : EntitySystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;

    private static readonly EntProtoId DefenseSparks = "EffectSparks";
    private static readonly SoundSpecifier SaberClash = new SoundPathSpecifier(
        "/Audio/_SW/Weapons/Melee/Lightsaber/lightsaber1.ogg",
        AudioParams.Default.WithVariation(0.25f).WithVolume(5f));

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWSaberFormComponent, SWSaberFormDefenseEvent>(OnDefense);
    }

    private void OnDefense(Entity<SWSaberFormComponent> ent, ref SWSaberFormDefenseEvent args)
    {
        Spawn(DefenseSparks, args.Coordinates);
        if (args.PlayClash)
            _audio.PlayPvs(SaberClash, args.Coordinates);
    }
}
