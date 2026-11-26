using Content.Shared.Mobs;

namespace Content.Server._SW.GlobalWar;

/// <summary>
/// In the campaign a soldier who falls stops looking at consoles: every window bound to the body closes, so that the
/// respawn window is the one thing on the screen.
/// </summary>
public sealed class SWGlobalWarDeathUiSystem : EntitySystem
{
    [Dependency] private readonly SWGlobalWarGameruleSystem _rules = default!;
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
    }

    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Alive || !_rules.TryGetGlobalWarGamerule(out _))
            return;

        _ui.CloseUserUis(args.Target);
    }
}
