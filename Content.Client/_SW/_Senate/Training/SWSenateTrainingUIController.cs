using System.Linq;
using Content.Client._SW._Senate.Intro;
using Content.Client.GameTicking.Managers;
using Content.Client.Lobby;
using Content.Shared.CCVar;
using Content.Shared.Preferences;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Client._SW._Senate.Training;

/// <summary>
/// Offers the Senate training in the lobby whenever the server reports a Senate round, including to a player who is
/// still waiting in the lobby after the round has started. The training is local to this client: it never sends the
/// ready state or any other command, and it closes when the client leaves the lobby.
/// </summary>
public sealed class SWSenateTrainingUIController : UIController, IOnStateEntered<LobbyState>, IOnStateExited<LobbyState>
{
    [Dependency] private readonly IConfigurationManager _config = default!;
    [Dependency] private readonly IClientPreferencesManager _preferences = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private SWSenateTraining? _training;
    private Button? _button;
    private bool _available;
    private bool _closeRequested;

    /// <summary>The Senate intro does not start itself over an open training.</summary>
    public bool IsOpen => _training != null;

    public void OnStateEntered(LobbyState state)
    {
        _available = EntityManager.System<SWSenateIntroSystem>().Available;
        var ready = state.Lobby?.ReadyButton;
        if (ready?.Parent is not { } row)
            return;

        _button = new Button { Text = Loc.GetString("sw-senate-training-lobby-button"), StyleClasses = { "ButtonBig" }, Visible = _available };
        _button.OnPressed += _ => Show();
        row.AddChild(_button);
    }

    public void OnStateExited(LobbyState state)
    {
        Close();
        _button?.Orphan();
        _button = null;
    }

    public override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (_button == null)
            return;

        var available = EntityManager.System<SWSenateIntroSystem>().Available;
        if (available != _available)
        {
            _available = available;
            _button.Visible = available;
            if (!available)
                Close();
        }

        // The training asks to close or navigate from its own input and update; both are applied here, outside its tree walk.
        if (_closeRequested)
        {
            _closeRequested = false;
            Close();
        }

        if (_training == null)
            return;

        _training.ApplyPending();
        _training.UpdateStatus(Countdown(EntityManager.System<ClientGameTicker>()));
    }

    private string Countdown(ClientGameTicker ticker)
    {
        if (ticker.IsGameStarted)
            return "Раунд уже идёт";
        if (ticker.Paused)
            return "Отсчёт приостановлен";

        var remaining = ticker.StartTime - _timing.CurTime;
        if (remaining <= TimeSpan.Zero)
            return "Раунд вот-вот начнётся";

        return $"До начала раунда: {(int) remaining.TotalMinutes}:{remaining.Seconds:00}";
    }

    private void Show()
    {
        if (!EntityManager.System<SWSenateIntroSystem>().Available ||
            _training != null ||
            UIManager.GetUIController<SWSenateIntroUIController>().IsOpen)
            return;

        _training = new SWSenateTraining(ResolveJob(), _config.GetCVar(CCVars.ReducedMotion));
        _training.CloseRequested += () => _closeRequested = true;
        UIManager.WindowRoot.AddChild(_training);
        LayoutContainer.SetAnchorPreset(_training, LayoutContainer.LayoutPreset.Wide);
    }

    private void Close()
    {
        _closeRequested = false;
        _training?.Orphan();
        _training = null;
    }

    /// <summary>The highest-priority job of the selected character, used only to list that occupation first.</summary>
    private string? ResolveJob()
    {
        if (_preferences.Preferences?.SelectedCharacter is not HumanoidCharacterProfile profile)
            return null;

        var job = profile.JobPriorities
            .Where(pair => pair.Value == JobPriority.High)
            .Select(pair => pair.Key)
            .FirstOrDefault();

        return job.Id;
    }
}
