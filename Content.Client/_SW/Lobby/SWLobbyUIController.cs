using System.Numerics;
using Content.Client._SW.GlobalWar.UI;
using Content.Client._SW.UserInterface;
using Content.Client.GameTicking.Managers;
using Content.Client.Lobby;
using Content.Client.Lobby.UI;
using Content.Shared._SW.Lobby;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._SW.Lobby;

/// <summary>Round facts as tiles, and the main action on the sword of the menu art at any window size.</summary>
public sealed class SWLobbyUIController : UIController, IOnStateEntered<LobbyState>, IOnStateExited<LobbyState>
{
    /// <summary>Where the sword stands in the menu art, as a share of the art's width.</summary>
    private const float SwordShare = 574f / 1890f;
    private const float SmallRootHeight = 620;
    private const float NarrowRootWidth = 1200;

    [UISystemDependency] private readonly ClientGameTicker _ticker = default!;

    private LobbyGui? _gui;
    private SWLobbyFactsEvent? _facts;
    private bool _shown;
    private SWGlobalWarStatTile? _round;
    private SWGlobalWarStatTile? _players;
    private SWGlobalWarStatTile? _map;
    private SWGlobalWarStatTile? _mode;
    private RichTextLabel? _description;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<SWLobbyFactsEvent>(OnFacts);
    }

    private void OnFacts(SWLobbyFactsEvent facts, EntitySessionEventArgs args)
    {
        _facts = facts;
        _shown = false;
    }

    public void OnStateEntered(LobbyState state)
    {
        if (UIManager.ActiveScreen is not LobbyGui gui)
            return;

        _gui = gui;
        _shown = false;
        gui.FactsSlot.RemoveAllChildren();
        var first = new BoxContainer { SeparationOverride = 6 };
        _round = new SWGlobalWarStatTile(Loc.GetString("sw-lobby-round"), 18);
        _players = new SWGlobalWarStatTile(Loc.GetString("sw-lobby-players"), 18);
        first.AddChild(_round);
        first.AddChild(_players);
        var second = new BoxContainer { SeparationOverride = 6 };
        _mode = new SWGlobalWarStatTile(Loc.GetString("sw-lobby-mode"), 14);
        _map = new SWGlobalWarStatTile(Loc.GetString("sw-lobby-map"), 14);
        second.AddChild(_mode);
        second.AddChild(_map);
        _description = new RichTextLabel();
        gui.FactsSlot.AddChild(first);
        gui.FactsSlot.AddChild(second);
        gui.FactsSlot.AddChild(_description);
        gui.TimerPlate.PanelOverride = gui.PlaytimePlate.PanelOverride = SWTerminalStyle.Fill(Color.FromHex("#07090E").WithAlpha(.9f));
        gui.ReadyButton.Label.FontOverride = SWGlobalWarConsoleStyle.Bold(26);
        gui.ReadyButton.Label.HorizontalAlignment = Control.HAlignment.Center;
    }

    public void OnStateExited(LobbyState state)
    {
        _gui = null;
    }

    public override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (_gui is not { Disposed: false } gui)
            return;

        Place(gui);
        gui.StationTime.Visible = _ticker.IsGameStarted;
        gui.TimerPlate.Visible = true;
        if (_shown)
            return;

        _shown = true;
        gui.ServerInfoBlock.Visible = _facts == null;
        gui.FactsSlot.Visible = _facts != null;
        if (_facts is not { } facts)
            return;

        _round!.Value.Text = $"#{facts.RoundId}";
        _players!.Value.Text = _ticker.IsGameStarted
            ? facts.Players.ToString()
            : Loc.GetString("sw-lobby-players-ready", ("players", facts.Players), ("ready", facts.Ready));
        _mode!.Value.Text = facts.Mode;
        _map!.Value.Text = facts.Map;
        // A round on the campaign's empty map has nothing to say here.
        _map.Visible = facts.Map.Length != 0 && facts.Map != "Empty";
        _description!.SetMessage(FormattedMessage.FromUnformatted(facts.Description), defaultColor: SWGlobalWarConsoleStyle.TextBright);
    }

    /// <summary>The cover stretch moves the sword on screens that are not 16:9, so it is measured every frame.</summary>
    private void Place(LobbyGui gui)
    {
        // The character setup takes the whole screen, as in the base game. The column kept its fixed width beside it,
        // so the base game's rule left it shown on wide screens, over the setup's tabs and its close button.
        if (gui.CharacterSetupState.Visible)
        {
            gui.RightSide.Visible = false;
            return;
        }

        var root = gui.Size;
        var small = root.Y < SmallRootHeight;
        var narrow = root.X < NarrowRootWidth;
        gui.RightSide.SetWidth = narrow ? 410 : 480;
        // The chat's own minimum is 465 wide: in the narrow column it ran out over the left edge.
        gui.Chat.MinWidth = 0;
        // A narrow column takes its four buttons in two rows, and its figures a size smaller, so that nothing is cut.
        gui.LobbyButtons.Columns = narrow ? 2 : 4;
        if (_round != null)
            _round.Value.FontOverride = SWGlobalWarConsoleStyle.Bold(narrow ? 15 : 18);
        if (_players != null)
            _players.Value.FontOverride = SWGlobalWarConsoleStyle.Bold(narrow ? 15 : 18);
        if (_mode != null)
            _mode.Value.FontOverride = SWGlobalWarConsoleStyle.Bold(narrow ? 12 : 14);
        if (_map != null)
            _map.Value.FontOverride = SWGlobalWarConsoleStyle.Bold(narrow ? 12 : 14);
        gui.FactsDescriptionVisible(!small);
        // Never narrower than the longest countdown and its plate's margins: the timer was cut at both ends.
        var width = Math.Max(small ? 320 : root.Y < 900 ? 420 : 560, (int) MathF.Ceiling(gui.StartTimeSizer.DesiredSize.X) + 24);
        gui.MainActions.SetWidth = width;
        gui.ReadyButton.MinHeight = small ? 52 : root.Y < 900 ? 72 : 88;

        var axis = Sword(gui, root);
        var leftWidth = gui.DefaultState.Size.X;
        var inset = gui.DefaultState.GlobalPosition.X - gui.GlobalPosition.X;
        var x = Math.Clamp(axis - inset - width / 2f, 0, Math.Max(0, leftWidth - width));
        gui.MainActions.Margin = new Thickness(x, 0, 0, gui.MainActions.Margin.Bottom);
        var links = gui.LinksPlate.Size.X;
        gui.LinksPlate.Margin = new Thickness(Math.Clamp(axis - inset - links / 2f, 0, Math.Max(0, leftWidth - links)), 0, 0, 0);
    }

    private static float Sword(LobbyGui gui, Vector2 root)
    {
        if (gui.Background.Texture is not { } art)
            return root.X * SwordShare;

        var size = (Vector2) art.Size;
        var scale = MathF.Max(root.X / size.X, root.Y / size.Y);
        return (root.X - size.X * scale) / 2 + size.X * scale * SwordShare;
    }
}
