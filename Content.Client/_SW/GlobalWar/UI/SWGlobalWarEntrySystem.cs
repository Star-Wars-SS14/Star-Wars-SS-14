using Content.Shared._SW.GlobalWar;
using Content.Shared.GameTicking;

namespace Content.Client._SW.GlobalWar.UI;

/// <summary>Client end of campaign entry: server answers to a late join, and the way back after death.</summary>
public sealed class SWGlobalWarEntrySystem : EntitySystem
{
    private SWGlobalWarRespawnWindow? _respawn;

    /// <summary>The server accepted or refused the last late-join request of this client.</summary>
    public event Action<SWGlobalWarJoinResultEvent>? JoinResult;

    public SWGlobalWarRespawnStateEvent? RespawnState { get; private set; }

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<SWGlobalWarJoinResultEvent>(args => JoinResult?.Invoke(args));
        SubscribeNetworkEvent<SWGlobalWarRespawnStateEvent>(OnRespawnState);
        SubscribeNetworkEvent<SWGlobalWarRespawnResultEvent>(OnRespawnResult);
        SubscribeNetworkEvent<RoundRestartCleanupEvent>(_ => CloseRespawn());
    }

    public override void Shutdown()
    {
        CloseRespawn();
        base.Shutdown();
    }

    private void OnRespawnState(SWGlobalWarRespawnStateEvent args)
    {
        if (args.Life == Guid.Empty)
        {
            CloseRespawn();
            return;
        }
        RespawnState = args;
        if (_respawn == null)
        {
            _respawn = new SWGlobalWarRespawnWindow();
            _respawn.OnChoice += (life, force) => RaiseNetworkEvent(new SWGlobalWarRespawnRequestEvent(life, force));
            _respawn.OpenCenteredRight();
        }
        _respawn.Update(args);
    }

    private void OnRespawnResult(SWGlobalWarRespawnResultEvent args)
    {
        if (args.Success)
            CloseRespawn();
        else
            _respawn?.ShowResult(args.Message);
    }

    private void CloseRespawn()
    {
        RespawnState = null;
        _respawn?.Close();
        _respawn = null;
    }
}
