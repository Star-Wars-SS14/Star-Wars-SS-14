using Content.Shared._SW._Senate.Economy;
using Robust.Client;
using Robust.Shared.Timing;

namespace Content.Client._SW._Senate.Economy;

/// <summary>Shared public cache. Opening or filtering a map never triggers a new economic calculation.</summary>
public sealed class SWRepublicEconomyClientSystem : EntitySystem
{
    [Dependency] private readonly IBaseClient _client = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    private TimeSpan _nextRequest;
    public SWEconomyView View { get; private set; } = new() { Paused = true };
    public event Action? Changed;

    public override void Initialize()
    {
        SubscribeNetworkEvent<SWRepublicEconomyUpdateEvent>(OnUpdated);
        _client.RunLevelChanged += OnRunLevelChanged;
    }

    public override void Shutdown()
    {
        _client.RunLevelChanged -= OnRunLevelChanged;
        View = new SWEconomyView { Paused = true };
        Changed = null;
        base.Shutdown();
    }

    public void Request()
    {
        if (_client.RunLevel is not (ClientRunLevel.Connected or ClientRunLevel.InGame)
            || _timing.RealTime < _nextRequest)
            return;
        _nextRequest = _timing.RealTime + TimeSpan.FromSeconds(2);
        RaiseNetworkEvent(new SWRepublicEconomyRequestEvent());
    }

    private void OnUpdated(SWRepublicEconomyUpdateEvent message)
    {
        if (message.View.Revision < View.Revision)
            return;
        View = message.View;
        Changed?.Invoke();
    }

    private void OnRunLevelChanged(object? sender, RunLevelChangedEventArgs args)
    {
        if (args.NewLevel is ClientRunLevel.Initialize or ClientRunLevel.Connecting)
        {
            View = new SWEconomyView { Paused = true };
            _nextRequest = TimeSpan.Zero;
            Changed?.Invoke();
        }
        else if (args.NewLevel is ClientRunLevel.Connected or ClientRunLevel.InGame)
            Request();
    }
}
