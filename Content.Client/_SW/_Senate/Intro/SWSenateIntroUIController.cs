using System.Linq;
using Content.Client.Audio;
using Content.Client.GameTicking.Managers;
using Content.Client.Lobby;
using Content.Shared._SW._Senate.Intro;
using Content.Shared.Preferences;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.Console;
using Robust.Shared.Timing;

namespace Content.Client._SW._Senate.Intro;

/// <summary>
/// Offers the Senate intro in the lobby: a manual button, and an automatic showing during the last minute before
/// the round when the server reports that the next round is a Senate round.
/// </summary>
public sealed class SWSenateIntroUIController : UIController, IOnStateEntered<LobbyState>, IOnStateExited<LobbyState>
{
    [Dependency] private readonly IConfigurationManager _config = default!;
    [Dependency] private readonly IClientPreferencesManager _preferences = default!;
    [Dependency] private readonly IConsoleHost _console = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    /// <summary>The intro is offered automatically only when at least this much of the countdown remains.</summary>
    private static readonly TimeSpan AutoplayWindow = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan AutoplayMinimum = TimeSpan.FromSeconds(8);

    private static readonly Dictionary<string, SWSenateIntroRole> Roles = new()
    {
        ["SWChancellor"] = new("Верховный канцлер", "Утвердите бюджет до 09:00 и решайте судьбу принятых законов: подпись или вето."),
        ["SWSpeaker"] = new("Спикер Сената", "Откройте рабочую консоль и поставьте законопроекты в расписание заседаний."),
        ["SWSenator"] = new("Сенатор", "Займите сектор в консоли представительства. Без сектора у вас нет голоса."),
        ["SWMinister"] = new("Министр", "Выберите ведомство в консоли министерств и возьмите в работу его задачи."),
        ["SWChiefJustice"] = new("Верховный судья", "Вступите в должность в суде. Слушания идут с 09:00 и с 15:00."),
        ["SWRepublicJudge"] = new("Судья Республики", "Вступите в должность в суде и рассматривайте дела вместе с коллегией."),
        ["SWProsecutor"] = new("Обвинитель Республики", "Вступите в должность и подготовьте линию обвинения к слушанию."),
        ["SWDefender"] = new("Защитник Республики", "Вступите в должность и подготовьте линию защиты к слушанию."),
        ["SWHolonetCorrespondent"] = new("Корреспондент Голонета", "Найдите терминал Голонета и рассказывайте Республике о главном."),
        ["SWUsher"] = new("Пристав Сената", "Примите смену в консоли смен и сопровождайте заседания."),
        ["SWSenateGuardHead"] = new("Глава Сенатской Гвардии", "Примите смену и распределите отряды по залу, коридорам и докам."),
        ["SWSenateGuardOfficer"] = new("Офицер Сенатской Гвардии", "Примите смену в консоли смен и следуйте указаниям главы Гвардии."),
        ["SWSenateDoctor"] = new("Сенатский врач", "Примите смену в консоли смен и будьте рядом с участниками созыва."),
    };

    private SWSenateIntro? _intro;
    private Button? _button;
    private TimeSpan? _autoShownFor;
    private bool _available;

    public void OnStateEntered(LobbyState state)
    {
        _available = EntityManager.System<SWSenateIntroSystem>().Available;
        var ready = state.Lobby?.ReadyButton;
        if (ready?.Parent is not { } row)
            return;

        _button = new Button { Text = "Интро Сената", StyleClasses = { "ButtonBig" }, Visible = _available };
        _button.OnPressed += _ => Show();
        row.AddChild(_button);
    }

    public void OnStateExited(LobbyState state)
    {
        Close(false);
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
                Close(true);
        }

        var ticker = EntityManager.System<ClientGameTicker>();
        if (_intro != null)
        {
            _intro.UpdateLobbyStatus(Countdown(ticker), ticker.AreWeReady, !ticker.IsGameStarted);
            return;
        }

        if (!_available || ticker.IsGameStarted || ticker.Paused || !_config.GetCVar(SWSenateIntroCVars.Autoplay))
            return;

        var remaining = ticker.StartTime - _timing.CurTime;
        if (remaining > AutoplayWindow || remaining < AutoplayMinimum || _autoShownFor == ticker.StartTime)
            return;

        _autoShownFor = ticker.StartTime;
        Show();
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
        if (_intro != null)
            return;

        _intro = new SWSenateIntro(ResolveRole(), _config.GetCVar(SWSenateIntroCVars.Autoplay));
        _intro.Finished += () => Close(true);
        _intro.ReadyToggled += ready =>
        {
            if (!EntityManager.System<ClientGameTicker>().IsGameStarted)
                _console.ExecuteCommand($"toggleready {ready}");
        };
        _intro.AutoplayToggled += enabled =>
        {
            _config.SetCVar(SWSenateIntroCVars.Autoplay, enabled);
            _config.SaveToFile();
        };

        UIManager.WindowRoot.AddChild(_intro);
        LayoutContainer.SetAnchorPreset(_intro, LayoutContainer.LayoutPreset.Wide);
        EntityManager.System<ContentAudioSystem>().StartSenateIntroMusic();
    }

    private void Close(bool resumeLobbyMusic)
    {
        if (_intro == null)
            return;

        _intro.Orphan();
        _intro = null;
        EntityManager.System<ContentAudioSystem>().StopSenateIntroMusic(resumeLobbyMusic);
    }

    /// <summary>The highest-priority job of the selected character; null when nothing is selected at high priority.</summary>
    private SWSenateIntroRole? ResolveRole()
    {
        if (_preferences.Preferences?.SelectedCharacter is not HumanoidCharacterProfile profile)
            return null;

        var job = profile.JobPriorities
            .Where(pair => pair.Value == JobPriority.High)
            .Select(pair => pair.Key)
            .FirstOrDefault();

        return job.Id != null && Roles.TryGetValue(job.Id, out var role) ? role : null;
    }
}
