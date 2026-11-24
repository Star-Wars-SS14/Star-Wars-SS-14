using Content.Shared._SW.GlobalWar;
using Content.Shared.GameTicking;

namespace Content.Client._SW.GlobalWar.UI;

/// <summary>Opens the campaign verdict over the ordinary round-end summary and closes it for the next round.</summary>
public sealed class SWGlobalWarRoundEndSystem : EntitySystem
{
    private SWGlobalWarRoundEndWindow? _window;

    /// <summary>The verdict of the round that just ended, until the next one starts.</summary>
    public SWGlobalWarRoundEndEvent? Last { get; private set; }

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<SWGlobalWarRoundEndEvent>(OnRoundEnd);
        SubscribeNetworkEvent<RoundRestartCleanupEvent>(_ => Reset());
    }

    public override void Shutdown()
    {
        Reset();
        base.Shutdown();
    }

    private void OnRoundEnd(SWGlobalWarRoundEndEvent args)
    {
        Last = args;
        _window?.Close();
        _window = new SWGlobalWarRoundEndWindow(args);
        _window.OpenCentered();
        _window.MoveToFront();
    }

    private void Reset()
    {
        Last = null;
        _window?.Close();
        _window = null;
    }
}
