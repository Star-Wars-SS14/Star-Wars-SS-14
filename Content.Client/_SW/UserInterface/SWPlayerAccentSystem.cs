using Content.Shared._SW.TaskSystem;
using Content.Shared.Ghost;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Shared.Player;

namespace Content.Client._SW.UserInterface;

/// <summary>
/// Tells the terminal style which faction the local player is in, for the <c>sw.ui_player_accent</c> option.
/// </summary>
public sealed class SWPlayerAccentSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IUserInterfaceManager _ui = default!;

    public override void Initialize()
    {
        base.Initialize();

        // Either can come first when a body spawns, because the component arrives with the job's special abilities.
        SubscribeLocalEvent<LocalPlayerAttachedEvent>(OnAttached);
        SubscribeLocalEvent<SWFactionMemberComponent, ComponentStartup>(OnMemberStartup);
    }

    private void OnAttached(LocalPlayerAttachedEvent args)
    {
        Update(args.Entity);
    }

    private void OnMemberStartup(Entity<SWFactionMemberComponent> ent, ref ComponentStartup args)
    {
        if (ent.Owner == _player.LocalEntity)
            Update(ent);
    }

    private void Update(EntityUid body)
    {
        // A ghost has no faction: dying must not swap the whole interface at a bad moment, so the last accent stays.
        if (HasComp<GhostComponent>(body))
            return;

        TryComp<SWFactionMemberComponent>(body, out var member);
        _ui.GetUIController<SWTerminalStyleUIController>().SetPlayerAccent(SWTerminalStyle.AccentOf(member?.Faction));
    }
}
