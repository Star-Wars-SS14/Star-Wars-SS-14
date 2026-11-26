using Content.Shared._SW._Senate.Intro;
using Robust.Client;

namespace Content.Client._SW._Senate.Intro;

/// <summary>Holds the server's statement that the selected mode is Senate.</summary>
public sealed class SWSenateIntroSystem : EntitySystem
{
    [Dependency] private readonly IBaseClient _client = default!;

    public bool Available { get; private set; }

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<SWSenateIntroStatusEvent>(OnStatus);
        _client.PlayerLeaveServer += OnLeave;
    }

    public override void Shutdown()
    {
        _client.PlayerLeaveServer -= OnLeave;
        base.Shutdown();
    }

    private void OnLeave(object? sender, PlayerEventArgs args)
    {
        Available = false;
    }

    private void OnStatus(SWSenateIntroStatusEvent ev)
    {
        Available = ev.Available;
    }
}
