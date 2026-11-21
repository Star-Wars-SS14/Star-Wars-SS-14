using Content.Shared._SW.SaberForms;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;

namespace Content.Server._SW.SaberForms;

public sealed class SWSaberFormDefenseVisualSystem : EntitySystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;

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
        if (args.Visual != SWSaberDefenseVisual.None)
        {
            var effect = new SWSaberDefenseEffectEvent(GetNetCoordinates(args.Coordinates), args.Visual,
                args.Direction, args.SourceDirection);
            var observers = Filter.Pvs(args.Coordinates, entityMan: EntityManager)
                .AddWhereAttachedEntity(actor => actor == ent.Owner);
            RaiseNetworkEvent(effect, observers);
        }

        if (args.PlayClash)
            _audio.PlayPvs(SaberClash, args.Coordinates);
    }
}
