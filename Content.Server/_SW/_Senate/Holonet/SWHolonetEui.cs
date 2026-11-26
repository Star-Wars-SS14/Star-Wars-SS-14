using Content.Server.EUI;
using Content.Shared._SW._Senate.Holonet;
using Content.Shared.Eui;

namespace Content.Server._SW._Senate.Holonet;

/// <summary>
/// Per-session terminal transport, needed because the mapped public screen has no Holonet interface key.
/// It only forwards requests: rights and content are decided by <see cref="SWHolonetSystem"/>.
/// </summary>
public sealed class SWHolonetEui : BaseEui
{
    [Dependency] private readonly IEntityManager _entities = default!;
    public readonly EntityUid Device;

    public SWHolonetEui(EntityUid device)
    {
        Device = device;
        IoCManager.InjectDependencies(this);
    }

    public override void Opened()
    {
        base.Opened();
        StateDirty();
    }

    public override void Closed()
    {
        _entities.System<SWHolonetSystem>().ForgetTerminal(Player.UserId, this);
        base.Closed();
    }

    public override EuiStateBase GetNewState() => new SWHolonetEuiState { Device = _entities.GetNetEntity(Device) };

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);
        if (msg is SWHolonetEuiMessage message && Player.AttachedEntity is { } actor)
            _entities.System<SWHolonetSystem>().HandleTerminal(this, actor, message.Request);
    }
}
