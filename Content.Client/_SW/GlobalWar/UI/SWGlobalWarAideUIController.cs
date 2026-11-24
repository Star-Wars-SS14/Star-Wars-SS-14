using System.Numerics;
using Content.Client._SW.Tutorial;
using Content.Client._SW.UserInterface;
using Content.Client.UserInterface.Systems.Gameplay;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Client._SW.GlobalWar.UI;

/// <summary>
/// A small card with the liaison officer and their line, just above where the campaign's hint plate stands, in the same
/// style. It stays clear of the chat, the status icons and an open inventory, and of the character in the middle.
/// </summary>
public sealed class SWGlobalWarAideUIController : UIController
{
    /// <summary>
    /// Over the left edge of the hands, like the hint plate.
    /// </summary>
    private const float LeftOfCenter = 190;

    /// <summary>
    /// Above a hint plate of one line.
    /// </summary>
    private const float AboveBottom = SWTutorialPrompt.AboveBottom + 32;

    private const float PortraitSize = 64;
    private const float CardWidth = 480;
    private const float CardHeight = 72;

    private static readonly Color Republic = Color.FromHex("#4FA3E8");
    private static readonly Color Empire = Color.FromHex("#D0303A");

    [Dependency] private readonly IGameTiming _timing = default!;

    private readonly Queue<SWGlobalWarAideEvent> _waiting = new();
    private Control? _card;
    private EntityUid? _officer;
    private TimeSpan _hideAt;

    public override void Initialize()
    {
        base.Initialize();
        UIManager.GetUIController<GameplayStateLoadController>().OnScreenUnload += Hide;
    }

    public void ResetConnection()
    {
        _waiting.Clear();
        Hide();
    }

    internal void OnAide(SWGlobalWarAideEvent ev, EntitySessionEventArgs args)
    {
        _waiting.Enqueue(ev);
    }

    public override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (_card != null && _timing.RealTime >= _hideAt)
            Hide();

        if (_card == null && _waiting.TryDequeue(out var next))
            Show(next);

        // Its time runs on behind a window, so a line does not come back long after its moment.
        if (_card != null)
            _card.Visible = !SWGlobalWarCoachUIController.WindowOver(UIManager, -LeftOfCenter, AboveBottom, CardWidth, CardHeight);
    }

    private void Show(SWGlobalWarAideEvent ev)
    {
        var side = ev.Side == SWGlobalWarFaction.Empire ? Empire : Republic;

        var lines = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            VerticalAlignment = Control.VAlignment.Center,
        };

        // One sentence to a line, so the card is only as wide as what it says.
        foreach (var sentence in ev.Message.Split(". ", StringSplitOptions.RemoveEmptyEntries))
        {
            lines.AddChild(new Label
            {
                Text = sentence.EndsWith('.') ? sentence : sentence + ".",
                StyleClasses = { SWTerminalStyle.Value },
            });
        }

        _officer = EntityManager.SpawnEntity(ev.Officer, MapCoordinates.Nullspace);
        var portrait = new SpriteView
        {
            SetSize = new Vector2(PortraitSize, PortraitSize),
            OverrideDirection = Direction.South,
            Stretch = SpriteView.StretchMode.None,
            Scale = new Vector2(2, 2),
        };
        portrait.SetEntity(_officer);

        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 6,
            Margin = new Thickness(4, 2, 8, 2),
        };
        row.AddChild(portrait);
        row.AddChild(lines);

        var card = new PanelContainer
        {
            HorizontalAlignment = Control.HAlignment.Left,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#0C0E0F").WithAlpha(0.92f),
                BorderColor = side,
                BorderThickness = new Thickness(1),
            },
        };
        card.AddChild(row);

        UIManager.PopupRoot.AddChild(card);
        LayoutContainer.SetAnchorPreset(card, LayoutContainer.LayoutPreset.CenterBottom);
        LayoutContainer.SetGrowHorizontal(card, LayoutContainer.GrowDirection.End);
        LayoutContainer.SetGrowVertical(card, LayoutContainer.GrowDirection.Begin);
        LayoutContainer.SetMarginLeft(card, -LeftOfCenter);
        LayoutContainer.SetMarginRight(card, -LeftOfCenter);
        LayoutContainer.SetMarginBottom(card, -AboveBottom);

        _card = card;
        _hideAt = _timing.RealTime + TimeSpan.FromSeconds(ev.Seconds);
    }

    private void Hide()
    {
        _card?.Orphan();
        _card = null;

        if (_officer is { } officer)
            EntityManager.DeleteEntity(officer);
        _officer = null;
    }
}
