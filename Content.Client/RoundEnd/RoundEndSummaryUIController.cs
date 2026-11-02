using Content.Client.GameTicking.Managers;
using Content.Client.Gameplay;
using Content.Client._SW._Senate;
using Content.Shared.GameTicking;
using Content.Shared.Input;
using JetBrains.Annotations;
using Robust.Client.Input;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;
using Content.Shared._SW._Senate;
using Robust.Shared.Input.Binding;
using Robust.Shared.Player;

namespace Content.Client.RoundEnd;

[UsedImplicitly]
public sealed class RoundEndSummaryUIController : UIController,
    IOnSystemLoaded<ClientGameTicker>, IOnSystemUnloaded<ClientGameTicker>, IOnStateExited<GameplayState>
{
    [Dependency] private readonly IInputManager _input = default!;

    private RoundEndSummaryWindow? _window;
    private SWSenateFinale? _finale;

    private void ToggleScoreboardWindow(ICommonSession? session = null)
    {
        if (_window == null)
            return;

        if (_finale != null)
        {
            ShowDetails();
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

    public void OpenRoundEndSummaryWindow(RoundEndMessageEvent message)
    {
        // Don't open duplicate windows (mainly for replays).
        if (_window?.RoundId == message.RoundId)
        {
            // An administrator can show a preliminary scoreboard before the Senate ends.
            if (_window.IsSenate || message.SenateHistory == null)
                return;
            _window.Close();
        }

        var history = message.SenateHistory;
        _window = new RoundEndSummaryWindow(message.GamemodeTitle, message.RoundEndText,
            message.RoundDuration, message.RoundId, message.AllPlayersEndInfo, EntityManager, history,
            history == null ? null : () => ShowFinale(history, message.RoundDuration));
        if (history != null)
            ShowFinale(history, message.RoundDuration);
        if (message.SenateHistory != null)
            EntityManager.System<SWSenatePersonalHistoryClientSystem>().Request();
    }

    public void OnSystemUnloaded(ClientGameTicker system)
    {
        RemoveFinale();
        _window?.Close();
        _window = null;
    }

    private void ShowFinale(SWSenateRoundHistory history, TimeSpan duration)
    {
        RemoveFinale();
        _window?.Close();
        _finale = new SWSenateFinale(history, duration, ShowDetails);
        UIManager.WindowRoot.AddChild(_finale);
        LayoutContainer.SetAnchorPreset(_finale, LayoutContainer.LayoutPreset.Wide);
    }

    private void RemoveFinale()
    {
        _finale?.Orphan();
        _finale = null;
    }

    private void ShowDetails()
    {
        RemoveFinale();
        if (_window == null) return;
        _window.OpenCenteredRight();
        _window.MoveToFront();
    }

    public void OnStateExited(GameplayState state) => RemoveFinale();

    public void OnSystemLoaded(ClientGameTicker system)
    {
        EntityManager.System<SWSenatePersonalHistoryClientSystem>().Received += lines => _window?.SetPersonalHistory(lines);
        _input.SetInputCommand(ContentKeyFunctions.ToggleRoundEndSummaryWindow,
            InputCmdHandler.FromDelegate(ToggleScoreboardWindow));
    }
}
