using Content.Client.GameTicking.Managers;
using Content.Client._SW._Senate;
using Content.Shared.GameTicking;
using Content.Shared.Input;
using JetBrains.Annotations;
using Robust.Client.Input;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared.Input.Binding;
using Robust.Shared.Player;

namespace Content.Client.RoundEnd;

[UsedImplicitly]
public sealed class RoundEndSummaryUIController : UIController,
    IOnSystemLoaded<ClientGameTicker>, IOnSystemUnloaded<ClientGameTicker>
{
    [Dependency] private readonly IInputManager _input = default!;

    private RoundEndSummaryWindow? _window;

    private void ToggleScoreboardWindow(ICommonSession? session = null)
    {
        if (_window == null)
            return;

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

        _window = new RoundEndSummaryWindow(message.GamemodeTitle, message.RoundEndText,
            message.RoundDuration, message.RoundId, message.AllPlayersEndInfo, EntityManager, message.SenateHistory);
        if (message.SenateHistory != null)
            EntityManager.System<SWSenatePersonalHistoryClientSystem>().Request();
    }

    public void OnSystemUnloaded(ClientGameTicker system)
    {
        _window?.Close();
        _window = null;
    }

    public void OnSystemLoaded(ClientGameTicker system)
    {
        EntityManager.System<SWSenatePersonalHistoryClientSystem>().Received += lines => _window?.SetPersonalHistory(lines);
        _input.SetInputCommand(ContentKeyFunctions.ToggleRoundEndSummaryWindow,
            InputCmdHandler.FromDelegate(ToggleScoreboardWindow));
    }
}
