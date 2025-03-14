using System.Linq;
using Content.Client.CharacterInfo;
using Content.Client.Gameplay;
using Content.Client.Stylesheets;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.Character.Controls;
using Content.Client.UserInterface.Systems.Character.Windows;
using Content.Client.UserInterface.Systems.Objectives.Controls;
using Content.Shared.Input;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Roles;
using JetBrains.Annotations;
using Robust.Client.GameObjects;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input.Binding;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using static Content.Client.CharacterInfo.CharacterInfoSystem;
using static Robust.Client.UserInterface.Controls.BaseButton;

namespace Content.Client.UserInterface.Systems.Character;

[UsedImplicitly]
public sealed class CharacterUIController : UIController, IOnStateEntered<GameplayState>, IOnStateExited<GameplayState>, IOnSystemChanged<CharacterInfoSystem>
{
    [Dependency] private readonly IEntityManager _ent = default!;
    [Dependency] private readonly ILogManager _logMan = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

    [UISystemDependency] private readonly CharacterInfoSystem _characterInfo = default!;
    [UISystemDependency] private readonly SpriteSystem _sprite = default!;

    private ISawmill _sawmill = default!;

    public override void Initialize()
    {
        base.Initialize();

        _sawmill = _logMan.GetSawmill("character");

        SubscribeNetworkEvent<MindRoleTypeChangedEvent>(OnRoleTypeChanged);
    }

    private CharacterWindow? _window;
    private MenuButton? CharacterButton => UIManager.GetActiveUIWidgetOrNull<MenuBar.Widgets.GameTopMenuBar>()?.CharacterButton;

    public void OnStateEntered(GameplayState state)
    {
        DebugTools.Assert(_window == null);

        _window = UIManager.CreateWindow<CharacterWindow>();
        LayoutContainer.SetAnchorPreset(_window, LayoutContainer.LayoutPreset.CenterTop);

        _window.OnClose += DeactivateButton;
        _window.OnOpen += ActivateButton;

        CommandBinds.Builder
            .Bind(ContentKeyFunctions.OpenCharacterMenu,
                InputCmdHandler.FromDelegate(_ => ToggleWindow()))
            .Register<CharacterUIController>();
    }

    public void OnStateExited(GameplayState state)
    {
        if (_window != null)
        {
            _window.Close();
            _window = null;
        }

        CommandBinds.Unregister<CharacterUIController>();
    }

    public void OnSystemLoaded(CharacterInfoSystem system)
    {
        system.OnCharacterUpdate += CharacterUpdated;
        _player.LocalPlayerDetached += CharacterDetached;
    }

    public void OnSystemUnloaded(CharacterInfoSystem system)
    {
        system.OnCharacterUpdate -= CharacterUpdated;
        _player.LocalPlayerDetached -= CharacterDetached;
    }

    public void UnloadButton()
    {
        if (CharacterButton == null)
        {
            return;
        }

        CharacterButton.OnPressed -= CharacterButtonPressed;
    }

    public void LoadButton()
    {
        if (CharacterButton == null)
        {
            return;
        }

        CharacterButton.OnPressed += CharacterButtonPressed;
    }

    private void DeactivateButton()
    {
        if (CharacterButton == null)
        {
            return;
        }

        CharacterButton.Pressed = false;
    }

    private void ActivateButton()
    {
        if (CharacterButton == null)
        {
            return;
        }

        CharacterButton.Pressed = true;
    }

private static readonly Dictionary<string, string> JobIdMapping = new Dictionary<string, string>
{
    { "лейтенант сил империи", "SWEmpireLieutenant" },
    { "майор сил империи", "SWEmpireMajor" },
    { "медик сил империи", "SWEmpireMedic" },
    { "мофф сил империи", "SWEmpireMoff" },
    { "солдат сил империи", "SWEmpireSoldier" },
    { "капитан сил республики", "SWRepublicCaptain" },
    { "генерал сил республики", "SWRepublicGeneral" },
    { "лейтенант сил республики", "SWRepublicLieutenant" },
    { "медик сил республики", "SWRepublicMedic" },
    { "рекрут сил республики", "SWRepublicRecruit" },
    { "солдат сил республики", "SWRepublicSoldier" },
    { "комендант корпуса", "SWSobrikCaptain" },
    { "офицер корпуса", "SWSobrikOfficer" },
    { "медик корпуса", "SWSobrikMedic" },
    { "лидер мандалорцев", "SWMandalorianLeader" },
    { "рекрут мандалорцев", "SWMandalorianRecruit" },
    { "ветеран мандалорцев", "SWMandalorianVeteran" },
    { "воин мандалорцев", "SWMandalorianWarrior" },
    { "агент имперской разведки", "SWEmpireIntelligenceAgent" },
    { "смотритель имперской разведки", "SWEmpireIntelligenceCaretaker" },
    { "командир-коммандос", "SWRepublicCommanderCommander" },
    { "лейтенант-коммандос", "SWRepublicCommanderLieutenant" },
    { "медик-коммандос", "SWRepublicCommanderMedic" },
    { "штурмовик-коммандос", "SWRepublicCommanderStormtrooper" },
    { "снайпер-коммандос", "SWRepublicCommanderSniper" },
    { "капитан черного солнца", "SWBlackSun" },
    { "наемник черного солнца", "SWBlackSunMercenary" },
    { "житель", "SWCivialian" },
    { "капитан обмена", "SWExchangeCaptain" },
    { "оперативник обмена", "SWExchangeOperative" },
};

private void CharacterUpdated(CharacterData data)
{
    if (_window == null)
    {
        return;
    }

    var (entity, job, objectives, briefing, entityName) = data;

    _window.SpriteView.SetEntity(entity);

    UpdateRoleType();

    _window.NameLabel.Text = entityName;
    _window.SubText.Text = job;
    _window.Objectives.RemoveAllChildren();
    _window.ObjectivesLabel.Visible = objectives.Any();

    string jobId = job.ToLower();

    if (JobIdMapping.TryGetValue(jobId, out var englishJobId))
    {
        jobId = englishJobId;
    }

    string jobDescriptionKey = "job-description-default";

    if (_prototypeManager.TryIndex<JobPrototype>(jobId, out var jobProto)) 
    {
        jobDescriptionKey = jobProto.Description ?? "job-description-default"; 
    }

    var jobDescription = Loc.GetString(jobDescriptionKey);

    var jobDescriptionLabel = new RichTextLabel
    {
        StyleClasses = { StyleNano.StyleClassTooltipActionTitle }
    };

    var formattedMessage = new FormattedMessage();
    formattedMessage.AddText(jobDescription); 

    jobDescriptionLabel.SetMessage(formattedMessage);

    _window.Objectives.AddChild(jobDescriptionLabel);

    foreach (var (groupId, conditions) in objectives)
    {
        var objectiveControl = new CharacterObjectiveControl
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Modulate = Color.Gray
        };

        var objectiveText = new FormattedMessage();
        objectiveText.TryAddMarkup(groupId, out _);

        var objectiveLabel = new RichTextLabel
        {
            StyleClasses = { StyleNano.StyleClassTooltipActionTitle }
        };
        objectiveLabel.SetMessage(objectiveText);

        objectiveControl.AddChild(objectiveLabel);

        foreach (var condition in conditions)
        {
            var conditionControl = new ObjectiveConditionsControl();
            conditionControl.ProgressTexture.Texture = _sprite.Frame0(condition.Icon);
            conditionControl.ProgressTexture.Progress = condition.Progress;
            var titleMessage = new FormattedMessage();
            var descriptionMessage = new FormattedMessage();
            titleMessage.AddText(condition.Title);
            descriptionMessage.AddText(condition.Description);

            conditionControl.Title.SetMessage(titleMessage);
            conditionControl.Description.SetMessage(descriptionMessage);

            objectiveControl.AddChild(conditionControl);
        }

        _window.Objectives.AddChild(objectiveControl);
    }

    if (briefing != null)
    {
        var briefingControl = new ObjectiveBriefingControl();
        var text = new FormattedMessage();
        text.PushColor(Color.Yellow);
        text.AddText(briefing);
        briefingControl.Label.SetMessage(text);
        _window.Objectives.AddChild(briefingControl);
    }

    var controls = _characterInfo.GetCharacterInfoControls(entity);
    foreach (var control in controls)
    {
        _window.Objectives.AddChild(control);
    }

    _window.RolePlaceholder.Visible = briefing == null && !controls.Any() && !objectives.Any();
}

    private void OnRoleTypeChanged(MindRoleTypeChangedEvent ev, EntitySessionEventArgs _)
    {
        UpdateRoleType();
    }

    private void UpdateRoleType()
    {
        if (_window == null || !_window.IsOpen)
            return;

        if (!_ent.TryGetComponent<MindContainerComponent>(_player.LocalEntity, out var container)
            || container.Mind is null)
            return;

        if (!_ent.TryGetComponent<MindComponent>(container.Mind.Value, out var mind))
            return;

        var roleText = Loc.GetString("role-type-crew-aligned-name");
        var color = Color.White;
        if (_prototypeManager.TryIndex(mind.RoleType, out var proto))
        {
            roleText = Loc.GetString(proto.Name);
            color = proto.Color;
        }

        _window.RoleType.Text = roleText;
        _window.RoleType.FontColorOverride = color;
    }

    private void CharacterDetached(EntityUid uid)
    {
        CloseWindow();
    }

    private void CharacterButtonPressed(ButtonEventArgs args)
    {
        ToggleWindow();
    }

    private void CloseWindow()
    {
        _window?.Close();
    }

    private void ToggleWindow()
    {
        if (_window == null)
            return;

        CharacterButton?.SetClickPressed(!_window.IsOpen);

        if (_window.IsOpen)
        {
            CloseWindow();
        }
        else
        {
            _characterInfo.RequestCharacterInfo();
            _window.Open();
        }
    }
}