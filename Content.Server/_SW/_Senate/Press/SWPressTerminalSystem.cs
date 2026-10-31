using System.Linq;
using Content.Server.EUI;
using Content.Shared.GameTicking;
using Robust.Shared.Network;
using Content.Shared.Interaction;
using Robust.Shared.Player;

namespace Content.Server._SW._Senate.Press;

[RegisterComponent]
public sealed partial class SWPressTerminalComponent : Component { }

public sealed class SWPressTerminalSystem : EntitySystem
{
    [Dependency] private readonly EuiManager _euis = default!;
    private readonly Dictionary<NetUserId, SWPressEui> _open = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<SWPressTerminalComponent, ActivateInWorldEvent>(OnActivate);
        SubscribeLocalEvent<SWPressTerminalComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => CloseAll());
    }

    private void CloseAll()
    {
        foreach (var eui in _open.Values.ToArray())
            if (!eui.IsShutDown) eui.Close();
        _open.Clear();
    }

    private void OnShutdown(Entity<SWPressTerminalComponent> ent, ref ComponentShutdown args)
    {
        foreach (var eui in _open.Values.Where(e => e.Terminal == ent.Owner).ToArray())
            if (!eui.IsShutDown) eui.Close();
    }

    public void Forget(NetUserId user, SWPressEui eui)
    {
        if (_open.TryGetValue(user, out var current) && current == eui) _open.Remove(user);
    }

    private void OnActivate(Entity<SWPressTerminalComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !TryComp<ActorComponent>(args.User, out var actor)
            || actor.PlayerSession.AttachedEntity != args.User)
            return;
        var user = actor.PlayerSession.UserId;
        if (_open.TryGetValue(user, out var previous) && !previous.IsShutDown) previous.Close();
        var eui = new SWPressEui(ent.Owner);
        _open[user] = eui;
        _euis.OpenEui(eui, actor.PlayerSession);
        args.Handled = true;
    }
}
