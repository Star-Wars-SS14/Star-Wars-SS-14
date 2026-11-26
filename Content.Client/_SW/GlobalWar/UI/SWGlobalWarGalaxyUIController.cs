using System.Linq;
using Content.Client.GameTicking.Managers;
using Content.Client.UserInterface.Systems.Gameplay;
using Content.Client.UserInterface.Systems.MenuBar.Widgets;
using Content.Shared._SW.GlobalWar;
using Content.Shared.Mobs.Systems;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared.Timing;

namespace Content.Client._SW.GlobalWar.UI;

/// <summary>A "Map" button in the top bar during the campaign, which opens the live galaxy map.</summary>
public sealed class SWGlobalWarGalaxyUIController : UIController
{
    private const string CampaignJobPrefix = "SWGlobalWar";

    [Dependency] private readonly IPlayerManager _player = default!;
    [UISystemDependency] private readonly ClientGameTicker _ticker = default!;
    [UISystemDependency] private readonly MobStateSystem _mobState = default!;

    private SWGlobalWarGalaxyWindow? _window;
    private SWGlobalWarGalaxyTopButton? _button;

    public override void Initialize()
    {
        base.Initialize();
        var load = UIManager.GetUIController<GameplayStateLoadController>();
        load.OnScreenLoad += OnScreenLoad;
        load.OnScreenUnload += () =>
        {
            _button?.Orphan();
            _button = null;
        };
        SubscribeNetworkEvent<SWGlobalWarGalaxyStateEvent>(OnState);
    }

    private bool Campaign() => _ticker.JobsAvailable.Values.Any(jobs => jobs.Keys.Any(job => job.Id.StartsWith(CampaignJobPrefix)));

    private void OnScreenLoad()
    {
        if (UIManager.ActiveScreen?.GetWidget<GameTopMenuBar>() is not { } menu)
            return;

        _button?.Orphan();
        _button = new SWGlobalWarGalaxyTopButton { Visible = Campaign() };
        _button.OnPressed += _ => Toggle();
        menu.AddChild(_button);
    }

    public override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (_button != null)
            _button.Visible = Campaign();

        // A fallen soldier sees the respawn window, not the chart.
        if (_window is { IsOpen: true } && _player.LocalEntity is { } body && _mobState.IsIncapacitated(body))
            _window.Close();
    }

    private void Toggle()
    {
        if (_window is { IsOpen: true })
        {
            _window.Close();
            return;
        }

        _window = new SWGlobalWarGalaxyWindow();
        _window.OnClose += () => EntityManager.RaisePredictiveEvent(new SWGlobalWarGalaxyRequestEvent(false));
        _window.OpenCentered();
        EntityManager.RaisePredictiveEvent(new SWGlobalWarGalaxyRequestEvent(true));
    }

    private void OnState(SWGlobalWarGalaxyStateEvent state, EntitySessionEventArgs args)
    {
        _window?.UpdateState(state.Worlds);
    }
}
