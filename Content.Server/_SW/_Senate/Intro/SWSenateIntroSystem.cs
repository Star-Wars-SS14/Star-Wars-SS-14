using Content.Server.GameTicking;
using Content.Shared._SW._Senate.Intro;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._SW._Senate.Intro;

/// <summary>Announces to lobby clients whether the next round is a Senate round. The server never starts the intro itself.</summary>
public sealed class SWSenateIntroSystem : EntitySystem
{
    public const string SenatePresetId = "SWSenatePreset";

    [Dependency] private readonly GameTicker _ticker = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private bool _available;
    private TimeSpan _nextCheck;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PlayerJoinedLobbyEvent>(OnJoinedLobby);
    }

    private bool IsSenateNext() => _ticker.Preset?.ID == SenatePresetId;

    private void OnJoinedLobby(PlayerJoinedLobbyEvent ev)
    {
        // Answer the joiner directly; the cached broadcast state is only advanced by Update.
        RaiseNetworkEvent(new SWSenateIntroStatusEvent(IsSenateNext()), ev.PlayerSession.Channel);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _nextCheck)
            return;

        _nextCheck = _timing.CurTime + TimeSpan.FromSeconds(1);
        var available = IsSenateNext();
        if (available == _available)
            return;

        _available = available;
        RaiseNetworkEvent(new SWSenateIntroStatusEvent(available), Filter.Broadcast());
    }
}
