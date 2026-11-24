using System.Numerics;
using Content.Client._SW.UserInterface;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._SW.BattlePass;

public enum SWBattlePassCellState : byte
{
    Empty,
    Future,
    Next,
    Claimable,
    Claimed,
}

/// <summary>
/// How much a reward weighs on the track. A pinch of snowflakes must not shout over a cape.
/// </summary>
public enum SWBattlePassCellWeight : byte
{
    Minor,
    Item,
    Milestone,
    Paid,
    PaidMilestone,
}

/// <summary>
/// One reward on the track. Future and locked rewards stay in full colour: what is on offer should be plain to see.
/// A colour never works alone here: received has a tick, available the word on a plate, closed a lock.
/// </summary>
public sealed class SWBattlePassCell : ContainerButton
{
    public const int CellWidth = 66;
    public const int CellHeight = 66;

    private const float TagHeight = 15;

    private static readonly Color Green = Color.FromHex("#5CB353");
    private static readonly Color Gold = Color.FromHex("#D9A441");
    private static readonly Color Ice = Color.FromHex("#9BE7FF");
    private static readonly Color Plate = Color.FromHex("#15181C");
    private static readonly Color Cold = Color.FromHex("#13242C");
    private static readonly Color Warm = Color.FromHex("#1C1508");

    private static readonly SpriteSpecifier.Rsi LockIcon = new(new ResPath(SWBattlePassWindow.IconRsi), "lock");
    private static readonly SpriteSpecifier.Texture TickIcon = new(new ResPath("/Textures/_SW/Interface/Misc/tick.png"));

    private readonly TextureRect _icon;
    private readonly EntityPrototypeView _entity;
    private readonly Label _word;
    private readonly TextureRect _corner;
    private readonly Label _tag;

    public SWBattlePassCell()
    {
        SetSize = new Vector2(CellWidth, CellHeight);
        MouseFilter = MouseFilterMode.Stop;

        _icon = new TextureRect
        {
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Center,
            Stretch = TextureRect.StretchMode.KeepAspectCentered,
            Visible = false,
            MouseFilter = MouseFilterMode.Ignore,
        };

        // Three times the sprite: the view clips the empty margins of its canvas, and the item fills the cell.
        // Without Stretch.None the view would shrink the canvas back to fit, margins and all.
        _entity = new EntityPrototypeView
        {
            SetSize = new Vector2(CellWidth - 4, CellHeight - 4),
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Center,
            Scale = new Vector2(3, 3),
            Stretch = SpriteView.StretchMode.None,
            Visible = false,
            MouseFilter = MouseFilterMode.Ignore,
        };
        _word = new Label
        {
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Bottom,
            StyleClasses = { SWTerminalStyle.Value },
            MouseFilter = MouseFilterMode.Ignore,
        };
        _corner = new TextureRect
        {
            SetSize = new Vector2(24, 24),
            HorizontalAlignment = HAlignment.Right,
            VerticalAlignment = VAlignment.Top,
            Stretch = TextureRect.StretchMode.KeepAspectCentered,
            Visible = false,
            MouseFilter = MouseFilterMode.Ignore,
        };
        _tag = new Label
        {
            Text = Loc.GetString("sw-battlepass-cell-claim"),
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 2),
            FontColorOverride = Green,
            StyleClasses = { SWTerminalStyle.CellWord },
            Visible = false,
            MouseFilter = MouseFilterMode.Ignore,
        };

        AddChild(_icon);
        AddChild(_entity);
        AddChild(_word);
        AddChild(_tag);
        AddChild(_corner);
    }

    /// <summary>
    /// Fills the cell.
    /// </summary>
    /// <param name="icon">The icon of a reward without an entity.</param>
    /// <param name="name">The reward's name for the tooltip.</param>
    /// <param name="amount">A count to show on the icon, for snowflakes.</param>
    /// <param name="state">What can be done with it.</param>
    /// <param name="weight">How much it weighs next to the others.</param>
    /// <param name="entity">The item's entity prototype, if it has one.</param>
    public void Set(Texture? icon, string name, int amount, SWBattlePassCellState state, SWBattlePassCellWeight weight, string? entity)
    {
        var claimable = state == SWBattlePassCellState.Claimable;
        var empty = state == SWBattlePassCellState.Empty;
        var lift = claimable ? TagHeight : 0;

        _icon.Texture = icon;
        _icon.Visible = icon != null;
        _icon.SetSize = amount > 0 ? new Vector2(28, 28) : new Vector2(60, 60);
        _icon.Margin = new Thickness(0, 0, 0, lift + (amount > 0 ? 16 : 0));

        _entity.Visible = entity != null;
        _entity.Margin = new Thickness(0, 0, 0, lift);
        if (entity != null)
            _entity.SetPrototype(entity);

        var modulate = state == SWBattlePassCellState.Claimed ? new Color(1f, 1f, 1f, 0.45f) : Color.White;
        _icon.ModulateSelfOverride = modulate;
        _entity.ModulateSelfOverride = modulate;

        _word.Text = amount > 0 ? $"+{amount}" : string.Empty;
        _word.Margin = new Thickness(0, 0, 0, lift + 2);
        _word.ModulateSelfOverride = modulate;
        _tag.Visible = claimable;

        var sprites = IoCManager.Resolve<IEntityManager>().System<SpriteSystem>();
        var claimed = state == SWBattlePassCellState.Claimed;
        _corner.Visible = claimed || state is SWBattlePassCellState.Future or SWBattlePassCellState.Next;
        _corner.Texture = sprites.Frame0(claimed ? TickIcon : LockIcon);
        _corner.SetSize = claimed ? new Vector2(18, 18) : new Vector2(24, 24);
        _corner.ModulateSelfOverride = claimed ? Green : null;

        Disabled = empty;
        ToolTip = string.IsNullOrEmpty(name) ? null : name;

        StyleBoxOverride = state switch
        {
            SWBattlePassCellState.Empty => new StyleBoxEmpty(),
            SWBattlePassCellState.Claimed => new StyleBoxEmpty(),
            SWBattlePassCellState.Claimable => SWTerminalStyle.Box(Plate, Green, 2),
            SWBattlePassCellState.Next => SWTerminalStyle.Box(weight is SWBattlePassCellWeight.Paid or SWBattlePassCellWeight.PaidMilestone ? Warm : Plate, Color.White, 2),
            _ => weight switch
            {
                SWBattlePassCellWeight.Minor => new StyleBoxEmpty(),
                SWBattlePassCellWeight.Milestone => SWTerminalStyle.Box(Cold, Ice, 2),
                SWBattlePassCellWeight.Paid => SWTerminalStyle.Box(Warm, Gold, 1),
                SWBattlePassCellWeight.PaidMilestone => SWTerminalStyle.Box(Warm, Gold, 2),
                _ => SWTerminalStyle.Box(Plate, SWTerminalStyle.CardBorder, 1),
            },
        };
    }
}
