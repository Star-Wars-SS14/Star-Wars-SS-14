using Content.Client.GameTicking.Managers;
using Content.Client.Gameplay;
using Content.Client.Audio;
using Content.Client._SW._Senate;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Input;
using JetBrains.Annotations;
using Robust.Client.Input;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;
using Content.Shared._SW._Senate;
using Robust.Shared.Configuration;
using Robust.Shared.Input.Binding;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Client.RoundEnd;

[UsedImplicitly]
public sealed class RoundEndSummaryUIController : UIController,
    IOnSystemLoaded<ClientGameTicker>, IOnSystemUnloaded<ClientGameTicker>, IOnStateExited<GameplayState>
{
    [Dependency] private readonly IInputManager _input = default!;
    [Dependency] private readonly IConfigurationManager _config = default!;
    [Dependency] private readonly IClientNetManager _net = default!;

    private RoundEndSummaryWindow? _window;
    private SWSenateFinale? _finale;
    private ClientGameTicker? _ticker;
    private SWSenatePersonalHistoryClientSystem? _personal;

    /// <summary>History of the round whose report is open; null once that round is over for this client.</summary>
    private SWSenateRoundHistory? _history;
    private TimeSpan _duration;
    private int _historyRound;

    /// <summary>
    /// Server (host peer + endpoint) and round whose outro already started automatically; kept across reconnects, so a resend of
    /// the same snapshot does not replay it. A matching round number of another server never suppresses autoplay.
    /// </summary>
    private (long Server, string Endpoint, int Round)? _autoplayedRound;

    /// <summary>
    /// Host peer identifier (Lidgren RemoteUniqueIdentifier, derived from the host's MAC and bound endpoint, not a
    /// per-run instance id) plus the server endpoint and the database-issued round id. Different servers never
    /// match, and an ordinary restart with the same database issues a new round id. Accepted limitation: after the
    /// database is reset or replaced on the same host and endpoint, a repeated round id suppresses the automatic
    /// outro; the current report is still opened. Null without a connection: then autoplay is not suppressed.
    /// </summary>
    private (long Server, string Endpoint, int Round)? AutoplayKey(int roundId) =>
        _net.ServerChannel is { } channel ? (channel.ConnectionId, channel.RemoteEndPoint.ToString(), roundId) : null;

    /// <summary>Set by the outro; the overlay is removed in <see cref="FrameUpdate"/>, outside the UI tree walk.</summary>
    private bool _finishRequested;

    private void ToggleScoreboardWindow(ICommonSession? session = null)
    {
        if (_window == null)
            return;

        if (_finale != null)
        {
            _finale.Finish();
            return;
        }

        if (_window.IsOpen)
        {
            _window.Close();
        }
        else
        {
            _window.OpenCenteredRight();
            if (_window.IsSenate) EntityManager.System<SWSenatePersonalHistoryClientSystem>().Request();
            _window.MoveToFront();
        }
    }

    //SW start: the campaign's verdict comes instead, and keeps this window behind a button.
    public void SWHideWindow() => _window?.Close();

    public void SWShowWindow()
    {
        if (_window is { IsOpen: false })
            ToggleScoreboardWindow();
    }
    //SW end

    public void OpenRoundEndSummaryWindow(RoundEndMessageEvent message)
    {
        // Don't open duplicate windows (mainly for replays).
        if (_window?.RoundId == message.RoundId)
        {
            if (_window.IsSenate)
            {
                // A resend of the final snapshot shows the existing report; a running outro is left alone.
                if (message.SenateHistory != null && _finale == null)
                    ShowDetails();
                return;
            }

            // An administrator can show a preliminary scoreboard before the Senate ends.
            if (message.SenateHistory == null)
                return;
            _window.Close();
        }

        // A report for another round replaces any outro still running.
        RemoveFinale();
        var history = message.SenateHistory;
        var roundId = message.RoundId;
        _history = history;
        _duration = message.RoundDuration;
        _historyRound = roundId;
        _window = new RoundEndSummaryWindow(message.GamemodeTitle, message.RoundEndText,
            message.RoundDuration, message.RoundId, message.AllPlayersEndInfo, EntityManager, history,
            history == null ? null : () => ReplayFinale(roundId),
            history == null ? null : () => EntityManager.System<SWSenatePersonalHistoryClientSystem>().Request());
        if (history == null)
            return;

        // The outro starts by itself once per server and round; a reconnect resend opens the report.
        var key = AutoplayKey(roundId);
        if (key == null || _autoplayedRound != key)
        {
            _autoplayedRound = key;
            ShowFinale();
        }
        else
        {
            ShowDetails();
        }

        EntityManager.System<SWSenatePersonalHistoryClientSystem>().Request();
    }

    public override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (!_finishRequested)
            return;

        _finishRequested = false;
        ShowDetails();
    }

    public void OnSystemUnloaded(ClientGameTicker system)
    {
        RemoveFinale();
        ForgetRound();
        _window?.Close();
        _window = null;
        if (_ticker != null)
            _ticker.LobbyStatusUpdated -= OnLobbyStatusUpdated;
        _ticker = null;
        if (_personal != null)
            _personal.Received -= OnPersonalHistory;
        _personal = null;
    }

    private void ReplayFinale(int roundId)
    {
        if (_history == null || _historyRound != roundId)
            return;

        if (_finale != null)
        {
            _finale.Restart();
            return;
        }

        ShowFinale();
    }

    private void ShowFinale()
    {
        if (_history == null || _finale != null)
            return;

        _finishRequested = false;
        _window?.Close();
        var audio = EntityManager.System<ContentAudioSystem>();
        var finale = new SWSenateFinale(_history, _duration, _config.GetCVar(CCVars.ReducedMotion));
        finale.Finished += () =>
        {
            if (_finale == finale)
                _finishRequested = true;
        };
        finale.PauseToggled += paused =>
        {
            if (_finale == finale)
                audio.SetSenateFinaleMusicPaused(paused);
        };
        finale.Replayed += () =>
        {
            if (_finale == finale)
                audio.StartSenateFinaleMusic();
        };
        _finale = finale;
        UIManager.WindowRoot.AddChild(finale);
        LayoutContainer.SetAnchorPreset(finale, LayoutContainer.LayoutPreset.Wide);
        audio.StartSenateFinaleMusic();
    }

    private void RemoveFinale()
    {
        _finishRequested = false;
        if (_finale == null)
            return;

        var finale = _finale;
        _finale = null;
        finale.Orphan();
        if (EntityManager.TrySystem(out ContentAudioSystem? audio))
            audio.StopSenateFinaleMusic();
    }

    private void ShowDetails()
    {
        RemoveFinale();
        if (_window == null) return;
        _window.OpenCenteredRight();
        _window.MoveToFront();
    }

    /// <summary>The round is over for this client: the old report may stay readable, but its outro can no longer play.</summary>
    private void ForgetRound()
    {
        _history = null;
        _window?.SetFinaleReplayAvailable(false);
    }

    public void OnStateExited(GameplayState state)
    {
        RemoveFinale();
        ForgetRound();
    }

    /// <summary>Clients that stayed in the lobby never leave GameplayState; the return to pre-round lobby ends the outro.</summary>
    private void OnLobbyStatusUpdated()
    {
        if (_ticker == null || _ticker.IsGameStarted)
            return;

        RemoveFinale();
        ForgetRound();
    }

    private void OnPersonalHistory(SWSenatePersonalHistoryResponse response) => _window?.SetPersonalHistory(response);

    public void OnSystemLoaded(ClientGameTicker system)
    {
        _ticker = system;
        system.LobbyStatusUpdated += OnLobbyStatusUpdated;
        _personal = EntityManager.System<SWSenatePersonalHistoryClientSystem>();
        _personal.Received += OnPersonalHistory;
        _input.SetInputCommand(ContentKeyFunctions.ToggleRoundEndSummaryWindow,
            InputCmdHandler.FromDelegate(ToggleScoreboardWindow));
    }
}
