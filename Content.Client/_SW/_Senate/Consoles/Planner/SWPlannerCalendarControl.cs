using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Shared._SW._Senate;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Maths;

namespace Content.Client._SW._Senate.Consoles.Planner;

/// <summary>A scrollable day view. It publishes intentions only; the menu owns all state and commands.</summary>
public sealed class SWPlannerCalendarControl : Control
{
    private readonly ScrollContainer _scroll;
    private readonly CalendarCanvas _canvas;
    public event Action<int, int>? RangeSelected;
    public event Action<SWPlannerItemEntry>? ItemSelected;
    public event Action<SWPlannerItemEntry, int>? ItemMoved;
    public event Action? DragCancelled;
    public bool HasDrag => _canvas.HasDrag;

    public SWPlannerCalendarControl()
    {
        HorizontalExpand = VerticalExpand = true;
        CanKeyboardFocus = true;
        KeyboardFocusOnClick = true;
        _scroll = new ScrollContainer { HorizontalExpand = true, VerticalExpand = true, HScrollEnabled = false, ReserveScrollbarSpace = true };
        _canvas = new CalendarCanvas(this);
        _scroll.AddChild(_canvas);
        AddChild(_scroll);
    }

    public void SetData(List<SWPlannerItemEntry> items, int nowSlot, bool canEdit, int selectedSlot,
        int selectedDuration, int maxDuration, HashSet<int> movableMeetingIds) =>
        _canvas.SetData(items, nowSlot, canEdit, selectedSlot, selectedDuration, maxDuration, movableMeetingIds);

    public void ScrollToSlot(int slot) => _scroll.VScroll = Math.Max(0, slot * CalendarCanvas.SlotHeight - 60);
    public bool CancelDrag() => _canvas.CancelDrag();

    private sealed class CalendarCanvas : Control
    {
        public const float SlotHeight = 22;
        private const float Gutter = 56;
        private readonly SWPlannerCalendarControl _owner;
        private readonly List<Card> _cards = new();
        private HashSet<int> _movable = new();
        private bool _canEdit;
        private int _now = -1, _selected, _duration = 2, _max = 4;
        private Vector2? _press;
        private int _pressSlot, _targetSlot;
        private bool _dragged;
        private int _sourceKind, _sourceId, _sourceStart, _sourceDuration;
        private bool _itemPress;
        private SWPlannerItemEntry? _pressedItem;
        public bool HasDrag => _press != null;

        private sealed class Card
        {
            public SWPlannerItemEntry Item = new();
            public int Column;
            public int Columns;
        }

        public CalendarCanvas(SWPlannerCalendarControl owner)
        {
            _owner = owner;
            HorizontalExpand = true;
            MinHeight = SWPlannerSlots.PerDay * SlotHeight + 4;
            MouseFilter = MouseFilterMode.Stop;
        }

        public void SetData(List<SWPlannerItemEntry> items, int now, bool canEdit, int selected,
            int duration, int max, HashSet<int> movable)
        {
            var lostEdit = _canEdit && !canEdit;
            _now = now;
            _canEdit = canEdit;
            _selected = selected;
            _max = Math.Clamp(max, 1, SWPlannerSlots.PerDay);
            _duration = Math.Clamp(duration, 1, _max);
            _movable = new HashSet<int>(movable);
            _cards.Clear();
            foreach (var source in items)
            {
                if (source.StartSlot < 0 || source.Duration <= 0 || source.StartSlot + source.Duration > SWPlannerSlots.PerDay)
                    continue;
                _cards.Add(new Card { Item = Copy(source) });
            }
            _cards.Sort((a, b) => a.Item.StartSlot.CompareTo(b.Item.StartSlot));
            // Every connected overlap group shares its column count, so no card obscures another.
            for (var first = 0; first < _cards.Count;)
            {
                var last = first + 1;
                var end = _cards[first].Item.StartSlot + _cards[first].Item.Duration;
                while (last < _cards.Count && _cards[last].Item.StartSlot < end)
                {
                    end = Math.Max(end, _cards[last].Item.StartSlot + _cards[last].Item.Duration);
                    last++;
                }
                var ends = new List<int>();
                for (var i = first; i < last; i++)
                {
                    var item = _cards[i].Item;
                    var col = 0;
                    while (col < ends.Count && ends[col] > item.StartSlot) col++;
                    if (col == ends.Count) ends.Add(0);
                    ends[col] = item.StartSlot + item.Duration;
                    _cards[i].Column = col;
                }
                for (var i = first; i < last; i++) _cards[i].Columns = ends.Count;
                first = last;
            }
            if (_press == null) return;
            if (!_itemPress)
            {
                if (lostEdit) CancelDrag();
                return;
            }
            if (_sourceId >= 0)
            {
                var current = FindSource();
                if (current == null || !Movable(current) || current.StartSlot != _sourceStart || current.Duration != _sourceDuration)
                    CancelDrag();
                else _pressedItem = current;
                return;
            }
            SWPlannerItemEntry? matching = null;
            foreach (var card in _cards)
            {
                var item = card.Item;
                if (_pressedItem != null && item.Kind == _pressedItem.Kind && item.MeetingId == _pressedItem.MeetingId &&
                    item.BlockId == _pressedItem.BlockId && item.StartSlot == _pressedItem.StartSlot &&
                    item.Duration == _pressedItem.Duration && item.Label == _pressedItem.Label && item.Sub == _pressedItem.Sub &&
                    item.Confirmed == _pressedItem.Confirmed)
                {
                    matching = item;
                    break;
                }
            }
            if (matching == null) CancelDrag();
            else _pressedItem = matching;
        }

        private static SWPlannerItemEntry Copy(SWPlannerItemEntry x) => new()
        {
            StartSlot = x.StartSlot, Duration = x.Duration, Kind = x.Kind, Label = x.Label, Sub = x.Sub,
            MeetingId = x.MeetingId, BlockId = x.BlockId, Confirmed = x.Confirmed,
        };

        private bool Movable(SWPlannerItemEntry item) => _canEdit &&
            (item.Kind == 3 && item.BlockId >= 0 || item.Kind == 2 && item.MeetingId >= 0 && _movable.Contains(item.MeetingId));
        private SWPlannerItemEntry? FindSource()
        {
            foreach (var card in _cards)
                if (card.Item.Kind == _sourceKind && (_sourceKind == 3 ? card.Item.BlockId : card.Item.MeetingId) == _sourceId)
                    return card.Item;
            return null;
        }
        private int Slot(float y) => Math.Clamp((int) MathF.Floor(y / SlotHeight), 0, SWPlannerSlots.PerDay - 1);
        private UIBox2 Box(Card card)
        {
            var width = Math.Max(1, Width - Gutter - 4) / card.Columns;
            return new UIBox2(Gutter + width * card.Column + 1, card.Item.StartSlot * SlotHeight + 1,
                Gutter + width * (card.Column + 1) - 1, (card.Item.StartSlot + card.Item.Duration) * SlotHeight - 1);
        }
        private SWPlannerItemEntry? Hit(Vector2 p)
        {
            foreach (var card in _cards)
            {
                var box = Box(card);
                if (p.X >= box.Left && p.X < box.Right && p.Y >= box.Top && p.Y < box.Bottom) return card.Item;
            }
            return null;
        }

        protected override void KeyBindDown(GUIBoundKeyEventArgs args)
        {
            base.KeyBindDown(args);
            if (args.Function != EngineKeyFunctions.UIClick && args.Function != EngineKeyFunctions.Use) return;
            _owner.GrabKeyboardFocus();
            _press = args.RelativePosition;
            _pressSlot = _targetSlot = Slot(args.RelativePosition.Y);
            _dragged = false;
            _pressedItem = Hit(args.RelativePosition);
            _itemPress = _pressedItem != null;
            _sourceId = -1;
            if (_pressedItem != null && Movable(_pressedItem))
            {
                _sourceKind = _pressedItem.Kind;
                _sourceId = _sourceKind == 3 ? _pressedItem.BlockId : _pressedItem.MeetingId;
                _sourceStart = _pressedItem.StartSlot;
                _sourceDuration = _pressedItem.Duration;
            }
            args.Handle();
        }

        protected override void MouseMove(GUIMouseMoveEventArgs args)
        {
            base.MouseMove(args);
            var hit = Hit(args.RelativePosition);
            ToolTip = hit == null ? null : $"{SWPlannerSlots.Range(hit.StartSlot, hit.Duration)}\n{hit.Label}\n{hit.Sub}";
            if (_press is not { } press) return;
            _dragged |= Vector2.DistanceSquared(press, args.RelativePosition) >= 25;
            if (_itemPress && _sourceId >= 0)
                _targetSlot = Math.Clamp(_sourceStart + Slot(args.RelativePosition.Y) - _pressSlot, 0, SWPlannerSlots.PerDay - _sourceDuration);
            else _targetSlot = Slot(args.RelativePosition.Y);
        }

        protected override void KeyBindUp(GUIBoundKeyEventArgs args)
        {
            base.KeyBindUp(args);
            if ((args.Function != EngineKeyFunctions.UIClick && args.Function != EngineKeyFunctions.Use) || _press == null) return;
            if (_itemPress)
            {
                if (_dragged && _sourceId >= 0 && _targetSlot != _sourceStart && FindSource() is { } current && Movable(current))
                    _owner.ItemMoved?.Invoke(Copy(current), _targetSlot);
                else if (!_dragged && _pressedItem != null) _owner.ItemSelected?.Invoke(Copy(_pressedItem));
            }
            else if (_dragged)
            {
                var start = Math.Min(_pressSlot, _targetSlot);
                var end = Math.Max(_pressSlot, _targetSlot);
                if (end - start + 1 > _max)
                    start = _targetSlot < _pressSlot ? _pressSlot - _max + 1 : _pressSlot;
                _owner.RangeSelected?.Invoke(start, Math.Min(_max, end - Math.Min(_pressSlot, _targetSlot) + 1));
            }
            else _owner.RangeSelected?.Invoke(Math.Min(_pressSlot, SWPlannerSlots.PerDay - _duration), _duration);
            _press = null;
            _pressedItem = null;
            args.Handle();
        }

        public bool CancelDrag()
        {
            if (_press == null) return false;
            _press = null;
            _pressedItem = null;
            _owner.DragCancelled?.Invoke();
            return true;
        }

        protected override void Draw(DrawingHandleScreen handle)
        {
            base.Draw(handle);
            var s = UIScale;
            handle.DrawRect(new UIBox2(0, 0, PixelWidth, PixelHeight), SWConsoleStyle.BgDark);
            for (var i = 0; i <= SWPlannerSlots.PerDay; i++)
            {
                var y = i * SlotHeight * s;
                handle.DrawLine(new Vector2(Gutter * s, y), new Vector2(PixelWidth, y), i % 4 == 0 ? SWConsoleStyle.Edge : SWConsoleStyle.BgRowAlt);
                if (i % 4 == 0) handle.DrawString(SWConsoleStyle.Small, new Vector2(2 * s, y + 12 * s), SWPlannerSlots.Time(i), s, SWConsoleStyle.TextDim);
            }
            if (_selected >= 0 && _selected < SWPlannerSlots.PerDay)
                Rect(handle, Gutter, _selected * SlotHeight, Width, Math.Min(SWPlannerSlots.PerDay, _selected + _duration) * SlotHeight, SWConsoleStyle.Selected);
            foreach (var card in _cards)
            {
                var box = Box(card);
                Rect(handle, box.Left, box.Top, box.Right, box.Bottom, SWConsoleStyle.BgRowAlt);
                Rect(handle, box.Left, box.Top, Math.Min(box.Right, box.Left + 3), box.Bottom, card.Item.Kind == 4 ? SWConsoleStyle.TextDim : SWConsoleStyle.Brass);
                // Text is limited to the available column; the full text remains available as a tooltip.
                var maxChars = Math.Max(0, (int)((box.Width - 10) / 8));
                var label = card.Item.Label;
                if (label.Length > maxChars) label = maxChars > 1 ? label.Substring(0, maxChars - 1) + "…" : string.Empty;
                if (maxChars > 0) handle.DrawString(SWConsoleStyle.Small, new Vector2((box.Left + 6) * s, (box.Top + 14) * s), label, s, SWConsoleStyle.TextBright);
                if (box.Height >= 38 && maxChars >= 11) handle.DrawString(SWConsoleStyle.Small, new Vector2((box.Left + 6) * s, (box.Top + 29) * s), SWPlannerSlots.Range(card.Item.StartSlot, card.Item.Duration), s, SWConsoleStyle.TextDim);
            }
            if (_press != null && _dragged && (!_itemPress || _sourceId >= 0))
            {
                var start = _itemPress ? _targetSlot : Math.Min(_pressSlot, _targetSlot);
                var length = _itemPress ? _sourceDuration : Math.Min(_max, Math.Abs(_targetSlot - _pressSlot) + 1);
                if (!_itemPress && _targetSlot < _pressSlot) start = _pressSlot - length + 1;
                Rect(handle, Gutter, start * SlotHeight, Width, (start + length) * SlotHeight, SWConsoleStyle.Selected);
                handle.DrawLine(new Vector2(Gutter * s, start * SlotHeight * s), new Vector2(PixelWidth, start * SlotHeight * s), SWConsoleStyle.Accent);
                handle.DrawString(SWConsoleStyle.Small, new Vector2((Gutter + 5) * s, (start * SlotHeight + 14) * s), SWPlannerSlots.Range(start, length), s, SWConsoleStyle.Accent);
            }
            if (_now >= 0 && _now <= SWPlannerSlots.PerDay)
                handle.DrawLine(new Vector2(Gutter * s, _now * SlotHeight * s), new Vector2(PixelWidth, _now * SlotHeight * s), SWConsoleStyle.Accent);
        }
        private void Rect(DrawingHandleScreen handle, float left, float top, float right, float bottom, Color color) =>
            handle.DrawRect(new UIBox2(left * UIScale, top * UIScale, right * UIScale, bottom * UIScale), color);
    }
}

