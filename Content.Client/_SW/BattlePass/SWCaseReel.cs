using System.Numerics;
using Content.Client._SW.UserInterface;
using Content.Shared._SW.BattlePass;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Client._SW.BattlePass;

/// <summary>
/// The case animation: a strip of items that slows down on the result the server already rolled. Only a show: the
/// result is rolled on the server before it starts, and nothing here can change it. The owner's decision: the strip
/// runs past the result towards a better item and is drawn back. This staged near miss is fit only for cases earned by
/// playing. If cases are ever sold, take it out. A player can skip the strip but cannot stop it.
/// </summary>
public sealed class SWCaseReel : Control
{
    private const int CellWidth = 120;
    private const int CellHeight = 141;
    private const int CellGap = 8;
    private const int EdgeWidth = 90;
    private const int TargetIndex = 48;
    private const int CellCount = 54;

    /// <summary>
    /// The run, a breath at the far point, and the pull back to the middle of the result.
    /// </summary>
    private const float RunTime = 3.3f;
    private const float HoldTime = 0.16f;
    private const float PullTime = 0.5f;
    private const float Duration = RunTime + HoldTime + PullTime;

    /// <summary>
    /// How far past the middle of the result the marker goes: almost to the next cell, never onto it.
    /// </summary>
    private const float OvershootMin = 44f;
    private const float OvershootMax = 57f;

    /// <summary>
    /// How much more often the item next to the result is the legendary one and not the rare one.
    /// </summary>
    private const float TeaseLegendaryChance = 0.67f;
    private const float SkipAfter = 0.5f;

    private static readonly Color Marker = Color.White;
    private static readonly Color Band = new(0.05f, 0.055f, 0.06f, 0.65f);
    private static readonly Color CellFill = Color.FromHex("#111418");

    [Dependency] private readonly IRobustRandom _random = default!;

    private readonly PanelContainer _background;
    private readonly PanelContainer _marker;
    private readonly Label _markerTop;
    private readonly Label _markerBottom;
    private readonly TextureRect _edgeLeft;
    private readonly TextureRect _edgeRight;
    private readonly BoxContainer _strip;
    private float _offset;

    private SWBattlePassCasePrototype? _case;
    private SWBattlePassWindow? _window;
    private float _time;
    private int _lastCell;
    private float _overshoot;
    private bool _pulling;
    private bool _cosmetic;
    private Control? _winner;

    /// <summary>
    /// Fires when the strip stops. The argument says whether the result is a cosmetic.
    /// </summary>
    public event Action<bool>? OnFinished;

    /// <summary>
    /// Fires each time another cell passes under the marker.
    /// </summary>
    public event Action? OnCellPassed;

    /// <summary>
    /// How far the spin has come, from 0 to 1.
    /// </summary>
    public float Progress => Spinning ? MathF.Min(1f, _time / Duration) : 0f;

    public bool Spinning { get; private set; }

    public bool CanSkip => _time >= SkipAfter;

    public SWCaseReel()
    {
        IoCManager.InjectDependencies(this);
        RectClipContent = true;
        MouseFilter = MouseFilterMode.Stop;

        _background = new PanelContainer { PanelOverride = SWTerminalStyle.Fill(Band) };
        AddChild(_background);

        _strip = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = CellGap,
        };
        AddChild(_strip);

        _edgeLeft = new TextureRect { TexturePath = "/Textures/_SW/Interface/BattlePass/track_fade_left.png", Stretch = TextureRect.StretchMode.Scale };
        _edgeRight = new TextureRect { TexturePath = "/Textures/_SW/Interface/BattlePass/track_fade.png", Stretch = TextureRect.StretchMode.Scale };
        AddChild(_edgeLeft);
        AddChild(_edgeRight);

        _marker = new PanelContainer { PanelOverride = SWTerminalStyle.Fill(Marker) };
        AddChild(_marker);

        _markerTop = new Label { Text = "▼", StyleClasses = { SWTerminalStyle.Value } };
        _markerBottom = new Label { Text = "▲", StyleClasses = { SWTerminalStyle.Value } };
        AddChild(_markerTop);
        AddChild(_markerBottom);
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        _background.Measure(availableSize);
        _marker.Measure(availableSize);
        _strip.Measure(new Vector2(float.PositiveInfinity, availableSize.Y));
        _edgeLeft.Measure(availableSize);
        _edgeRight.Measure(availableSize);
        _markerTop.Measure(availableSize);
        _markerBottom.Measure(availableSize);
        return new Vector2(0, CellHeight + 24);
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        _background.Arrange(UIBox2.FromDimensions(Vector2.Zero, finalSize));
        _marker.Arrange(UIBox2.FromDimensions(new Vector2(finalSize.X / 2 - 1, 0), new Vector2(2, finalSize.Y)));
        _strip.Arrange(UIBox2.FromDimensions(new Vector2(_offset, 12), _strip.DesiredSize));
        _edgeLeft.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(EdgeWidth, finalSize.Y)));
        _edgeRight.Arrange(UIBox2.FromDimensions(new Vector2(finalSize.X - EdgeWidth, 0), new Vector2(EdgeWidth, finalSize.Y)));
        _markerTop.Arrange(UIBox2.FromDimensions(new Vector2((finalSize.X - _markerTop.DesiredSize.X) / 2, -4), _markerTop.DesiredSize));
        _markerBottom.Arrange(UIBox2.FromDimensions(new Vector2((finalSize.X - _markerBottom.DesiredSize.X) / 2, finalSize.Y - _markerBottom.DesiredSize.Y + 4), _markerBottom.DesiredSize));
        return finalSize;
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);

        if (args.Function == EngineKeyFunctions.UIClick && CanSkip)
            Skip();
    }

    public void SetCase(SWBattlePassCasePrototype proto, SWBattlePassWindow window)
    {
        _case = proto;
        _window = window;

        if (!Spinning && _strip.ChildCount == 0)
            Fill(-1);
    }

    /// <summary>
    /// Starts the strip towards a line of the case.
    /// </summary>
    public void Spin(int entry, bool cosmetic)
    {
        Fill(entry);
        _cosmetic = cosmetic;
        _time = 0;
        _lastCell = 0;
        _pulling = false;
        _overshoot = OvershootMin + _random.NextFloat() * (OvershootMax - OvershootMin);
        Spinning = true;
    }

    /// <summary>
    /// Jumps to the result at once.
    /// </summary>
    public void Skip()
    {
        if (!Spinning)
            return;

        _time = Duration;
        Finish();
    }

    private void Finish()
    {
        Spinning = false;
        _offset = Rest();
        InvalidateArrange();

        foreach (var child in _strip.Children)
        {
            child.Modulate = child == _winner ? Color.White : new Color(1f, 1f, 1f, 0.3f);
        }

        OnFinished?.Invoke(_cosmetic);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (!Spinning)
            return;

        _time += args.DeltaSeconds;
        if (_time >= Duration)
        {
            Finish();
            return;
        }

        _offset = OffsetAt(_time);
        InvalidateArrange();

        // The click of the strip being caught.
        if (!_pulling && _time >= RunTime + HoldTime)
        {
            _pulling = true;
            OnCellPassed?.Invoke();
        }

        var cell = (int) ((Size.X / 2f - _offset) / (CellWidth + CellGap));
        if (cell != _lastCell)
        {
            _lastCell = cell;
            OnCellPassed?.Invoke();
        }
    }

    private float Rest()
    {
        return -(TargetIndex * (CellWidth + CellGap) + CellWidth / 2f - Size.X / 2f);
    }

    private float OffsetAt(float time)
    {
        var far = Rest() - _overshoot;
        if (time < RunTime)
        {
            // Quadratic out: the last second still crosses cells, so each next one can be read and hoped for.
            // A steeper curve looks slower but sits still at the end, with nothing left to wait for.
            var left = 1f - time / RunTime;
            return far * (1f - left * left);
        }

        if (time < RunTime + HoldTime)
            return far;

        // Back out: a pull that goes a little past the middle and settles, as a magnet does.
        const float back = 1.70158f;
        var u = MathF.Min(1f, (time - RunTime - HoldTime) / PullTime) - 1f;
        return far + _overshoot * (1f + (back + 1f) * u * u * u + back * u * u);
    }

    private void Fill(int target)
    {
        _strip.RemoveAllChildren();
        _winner = null;
        _offset = 0;
        InvalidateArrange();

        if (_case == null || _window == null)
            return;

        for (var i = 0; i < CellCount; i++)
        {
            var entry = i == TargetIndex && target >= 0 ? target
                : i == TargetIndex + 1 && target >= 0 ? PickTease(target)
                : PickFiller();
            var cell = BuildCell(_case.Entries[entry]);
            _strip.AddChild(cell);
            if (i == TargetIndex)
                _winner = cell;
        }
    }

    /// <summary>
    /// The item the strip almost reaches: something better than what the player really got.
    /// </summary>
    private int PickTease(int target)
    {
        var entries = _case!.Entries;
        var won = entries[target];
        var wonRank = won.Cosmetic == null ? -1 : (int) won.Rarity;
        var wanted = _random.NextFloat() < TeaseLegendaryChance ? SWCosmeticRarity.Legendary : SWCosmeticRarity.Rare;

        for (var i = 0; i < entries.Count; i++)
        {
            if (entries[i].Cosmetic != null && entries[i].Rarity == wanted && (int) wanted > wonRank)
                return i;
        }

        // Nothing fits, so it is one of the better ones if any, and an ordinary cell for the best result.
        for (var i = 0; i < entries.Count; i++)
        {
            if (entries[i].Cosmetic != null && (int) entries[i].Rarity > wonRank)
                return i;
        }

        return PickFiller();
    }

    private int PickFiller()
    {
        var entries = _case!.Entries;
        var roll = _random.NextFloat() * 100f;
        for (var i = 0; i < entries.Count; i++)
        {
            roll -= entries[i].Chance;
            if (roll < 0)
                return i;
        }

        return 0;
    }

    private Control BuildCell(SWBattlePassCaseEntry entry)
    {
        var color = SWBattlePassWindow.EntryColor(entry);
        var panel = new PanelContainer
        {
            SetSize = new Vector2(CellWidth, CellHeight),
            PanelOverride = SWTerminalStyle.Box(CellFill, entry.Cosmetic == null ? SWTerminalStyle.CardBorder : color, 2),
        };

        if (_window!.EntryEntity(entry) is { } entity)
        {
            var view = new EntityPrototypeView
            {
                SetSize = new Vector2(CellWidth - 6, 100),
                Scale = new Vector2(4, 4),
                Stretch = SpriteView.StretchMode.None,
                HorizontalAlignment = HAlignment.Center,
                VerticalAlignment = VAlignment.Top,
                Margin = new Thickness(0, 6, 0, 0),
            };
            view.SetPrototype(entity);
            panel.AddChild(view);
        }
        else
        {
            panel.AddChild(new TextureRect
            {
                Texture = _window.EntryIcon(),
                SetSize = new Vector2(84, 84),
                HorizontalAlignment = HAlignment.Center,
                VerticalAlignment = VAlignment.Top,
                Margin = new Thickness(0, 14, 0, 0),
                Stretch = TextureRect.StretchMode.KeepAspectCentered,
            });
        }

        panel.AddChild(new Label
        {
            Text = _window.EntryMark(entry),
            FontColorOverride = color,
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 12),
            StyleClasses = { SWTerminalStyle.Value },
        });
        panel.AddChild(new PanelContainer
        {
            MinHeight = 5,
            VerticalAlignment = VAlignment.Bottom,
            PanelOverride = SWTerminalStyle.Fill(color),
        });

        return panel;
    }
}
