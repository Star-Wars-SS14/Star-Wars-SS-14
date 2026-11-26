using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Shared._SW._Senate;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Input;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Client._SW._Senate.Consoles.Work;

/// <summary>Local schedule editor. It publishes intentions and never sends commands.</summary>
public sealed class SWSenateScheduleBoard : Control
{
    private const float Column = 220, Gutter = 58, HourHeight = 42, CardHeight = 54;
    private float Header => Math.Max(25, SWConsoleStyle.Small.GetHeight(UIScale) / UIScale + 8);
    private IReadOnlyList<SWScheduleRow> _rows = Array.Empty<SWScheduleRow>();
    private IReadOnlyList<SWBillOption> _bills = Array.Empty<SWBillOption>();
    private IReadOnlyList<int> _free = Array.Empty<int>();
    private bool _canSchedule;
    private float _billScroll, _timeScroll;
    private int? _selectedBill;
    private Vector2? _press;
    private Vector2 _pointer;
    private int _mode, _dragBill, _dragStart, _dragDuration, _pressHour, _targetStart, _targetDuration;
    private bool _dragged;
    private float _notice;
    public event Action<int>? BillSelected;
    public event Action<int, int, int>? DraftChanged;
    public event Action<int>? SessionSelected;
    public event Action? DraftCleared;
    public int? DraftBillId { get; private set; }
    public int? DraftStartHour { get; private set; }
    public int DraftDuration { get; private set; } = 1;
    public bool DraftValid => DraftBillId is { } id && DraftStartHour is { } hour && HasBill(id) && ValidSpan(hour, DraftDuration);

    public SWSenateScheduleBoard()
    {
        HorizontalExpand = true;
        VerticalExpand = true;
        MouseFilter = MouseFilterMode.Stop;
        CanKeyboardFocus = true;
        KeyboardFocusOnClick = true;
    }

    public void SetData(IReadOnlyList<SWScheduleRow> rows, IReadOnlyList<SWBillOption> bills, IReadOnlyList<int> freeHours, bool canSchedule)
    {
        _rows = rows; _bills = bills; _free = freeHours; _canSchedule = canSchedule;
        if (!canSchedule || (_press != null && !HasBill(_dragBill))) CancelInteraction();
        ClampScroll();
    }
    public void SetDraft(int billId, int startHour, int duration)
    {
        CancelInteraction(); DraftBillId = billId; DraftStartHour = startHour; DraftDuration = duration; _selectedBill = billId;
    }
    public void ClearDraft() { CancelInteraction(); DraftBillId = null; DraftStartHour = null; DraftDuration = 1; }
    public void ResetLocalState() { ClearDraft(); _selectedBill = null; _billScroll = _timeScroll = _notice = 0; }
    public void CancelInteraction() { _press = null; _mode = 0; _dragged = false; }
    private bool HasBill(int id) { foreach (var b in _bills) if (b.Id == id && !b.Emergency) return true; return false; }
    private bool ValidSpan(int start, int duration)
    {
        if (duration < 1 || duration > 4) return false;
        for (var h = start; h < start + duration; h++)
        {
            var row = false; var free = false;
            foreach (var r in _rows) if (r.Hour == h) { row = true; break; }
            foreach (var f in _free) if (f == h) { free = true; break; }
            if (!row || !free) return false;
        }
        return true;
    }
    private float ViewHeight => Math.Max(0, Height - Header);
    private int FirstHour => _rows.Count == 0 ? 0 : _rows[0].Hour;
    private int BillCount { get { var n = 0; foreach (var b in _bills) if (!b.Emergency) n++; return n; } }
    private void ClampScroll()
    {
        _billScroll = Math.Clamp(_billScroll, 0, Math.Max(0, BillCount * CardHeight - ViewHeight));
        _timeScroll = Math.Clamp(_timeScroll, 0, Math.Max(0, _rows.Count * HourHeight - ViewHeight));
    }
    private int HourAt(float y) => FirstHour + (int)MathF.Floor((y - Header + _timeScroll) / HourHeight);
    private float HourY(double hour) => Header + (float)(hour - FirstHour) * HourHeight - _timeScroll;
    private SWBillOption? BillAt(float y)
    {
        var index = (int)MathF.Floor((y - Header + _billScroll) / CardHeight);
        if (index < 0) return null;
        foreach (var b in _bills) if (!b.Emergency && index-- == 0) return b;
        return null;
    }
    private SWScheduleRow? SessionAt(Vector2 p)
    {
        if (p.X < Column + Gutter || p.Y < Header) return null;
        foreach (var r in _rows)
            if (r.SessionId != null && p.Y >= HourY(r.SessionStartHour) && p.Y < HourY(r.SessionStartHour + r.SessionDuration)) return r;
        return null;
    }
    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        if (args.Function == EngineKeyFunctions.TextDelete && _canSchedule && DraftBillId != null) { ClearDraft(); DraftCleared?.Invoke(); args.Handle(); return; }
        if (args.Function == EngineKeyFunctions.EscapeMenu && _press != null) { CancelInteraction(); args.Handle(); return; }
        if (args.Function != EngineKeyFunctions.UIClick && args.Function != EngineKeyFunctions.Use) return;
        var p = args.RelativePosition;
        if (p.Y < Header) return;
        GrabKeyboardFocus();
        var overDraft = p.X >= Column + Gutter + 4 && p.X < Width - 7 &&
            DraftBillId != null && DraftStartHour is { } draftStart &&
            p.Y >= Math.Max(Header, HourY(draftStart)) + 3 && p.Y < Math.Min(Height, HourY(draftStart + DraftDuration)) - 3;
        if ((!_canSchedule || !overDraft) && p.X >= Column && SessionAt(p) is { SessionId: { } session })
        { SessionSelected?.Invoke(session); args.Handle(); return; }
        if (!_canSchedule) return;
        _mode = 0;
        if (p.X < Column && BillAt(p.Y) is { } bill)
        {
            _selectedBill = bill.Id; BillSelected?.Invoke(bill.Id); _dragBill = bill.Id; _dragDuration = 1; _mode = 1;
        }
        else if (overDraft && DraftBillId is { } draft && DraftStartHour is { } start)
        {
            _dragBill = draft; _dragStart = start; _dragDuration = DraftDuration; _pressHour = HourAt(p.Y);
            _mode = p.Y >= HourY(start + DraftDuration) - 9 ? 3 : 2;
        }
        else if (p.X >= Column + Gutter && _selectedBill is { } selected && HasBill(selected))
        {
            _dragBill = selected; _dragDuration = 1; _mode = 4;
        }
        if (_mode == 0) return;
        _press = _pointer = p; _dragged = false; UpdateTarget(p); args.Handle();
    }
    private void UpdateTarget(Vector2 p)
    {
        var hour = HourAt(p.Y);
        _targetStart = _mode == 2 ? _dragStart + hour - _pressHour : _mode == 3 ? _dragStart : hour;
        _targetDuration = _mode == 3 ? Math.Clamp(hour - _dragStart + 1, 1, 4) : _dragDuration;
    }
    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args); _pointer = args.RelativePosition;
        if (_press is not { } p) return;
        _dragged |= Vector2.DistanceSquared(p, _pointer) >= 25;
        UpdateTarget(_pointer);
    }
    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);
        if ((args.Function != EngineKeyFunctions.UIClick && args.Function != EngineKeyFunctions.Use) || _press == null) return;
        var p = args.RelativePosition; UpdateTarget(p);
        var drop = p.X >= Column + Gutter && p.X < Width && p.Y >= Header && p.Y < Height && (_mode == 4 || _dragged);
        if (drop && ValidSpan(_targetStart, _targetDuration) && HasBill(_dragBill))
        {
            DraftBillId = _dragBill; DraftStartHour = _targetStart; DraftDuration = _targetDuration;
            DraftChanged?.Invoke(_dragBill, _targetStart, _targetDuration);
        }
        else if (drop) _notice = 2.5f;
        CancelInteraction(); args.Handle();
    }
    protected override void MouseWheel(GUIMouseWheelEventArgs args)
    {
        base.MouseWheel(args);
        if (args.RelativePosition.X < Column) _billScroll -= args.Delta.Y * CardHeight;
        else _timeScroll -= args.Delta.Y * HourHeight;
        ClampScroll(); if (_press != null) UpdateTarget(_pointer); args.Handle();
    }
    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args); _notice = Math.Max(0, _notice - args.DeltaSeconds);
        if (_press == null || !_dragged || _pointer.X < Column) return;
        var speed = Math.Clamp(args.DeltaSeconds, 0, .1f) * 200;
        if (_pointer.Y < Header + 30) _timeScroll -= speed;
        else if (_pointer.Y > Height - 30) _timeScroll += speed;
        ClampScroll(); UpdateTarget(_pointer);
    }
    protected override void KeyboardFocusExited() { base.KeyboardFocusExited(); CancelInteraction(); }
    protected override void VisibilityChanged(bool visible) { base.VisibilityChanged(visible); if (!visible) CancelInteraction(); }
    protected override void Dispose(bool disposing) { CancelInteraction(); base.Dispose(disposing); }
    protected override void Draw(DrawingHandleScreen h)
    {
        base.Draw(h); ClampScroll();
        Rect(h, 0, 0, Width, Height, SWConsoleStyle.BgDark);
        Text(h, 5, 4, "Законопроекты", SWConsoleStyle.TextBright);
        Text(h, Column + 5, 4, _notice > 0 ? "Недоступное время" : "Расписание дня", _notice > 0 ? SWConsoleStyle.Bad : SWConsoleStyle.TextBright);
        var i = 0;
        foreach (var b in _bills)
        {
            if (b.Emergency) continue;
            var y = Header + i++ * CardHeight - _billScroll;
            if (y < Header || y + CardHeight > Height) continue;
            var sourceDragging = _press != null && _dragged && _mode == 1 && b.Id == _dragBill;
            Rect(h, 2, y + 2, Column - 4, y + CardHeight - 2, sourceDragging ? SWConsoleStyle.BgDark : b.Id == _selectedBill ? SWConsoleStyle.Selected : SWConsoleStyle.BgRowAlt);
            Rect(h, 2, y + 2, 5, y + CardHeight - 2, sourceDragging ? SWConsoleStyle.TextDim : b.Id == _selectedBill ? SWConsoleStyle.Accent : SWConsoleStyle.Brass);
            Text(h, 10, y + 4, $"№{b.Id}", sourceDragging ? SWConsoleStyle.TextDim : SWConsoleStyle.Brass);
            Text(h, 10, y + 25, Short(b.Name, (int)((Column - 20) / 8)), sourceDragging ? SWConsoleStyle.TextDim : SWConsoleStyle.TextBright);
        }
        foreach (var r in _rows)
        {
            var y = HourY(r.Hour); if (y + HourHeight <= Header || y >= Height) continue;
            Rect(h, Column, Math.Max(Header, y), Width, Math.Min(Height, y + HourHeight - 1), r.IsNow ? SWConsoleStyle.Selected : r.Free ? SWConsoleStyle.BgPanel : SWConsoleStyle.BgRowAlt);
            if (y >= Header && y + 18 < Height) Text(h, Column + 4, y + 10, r.TimeLabel, SWConsoleStyle.TextDim);
            if (r.SessionId == null && !r.Free && y >= Header && y + 18 < Height) Text(h, Column + Gutter + 6, y + 10, Short(r.Text, (int)((Width - Column - Gutter - 12) / 8)), r.Color);
        }
        var seen = new HashSet<int>();
        foreach (var r in _rows)
        {
            if (r.SessionId is not { } id || !seen.Add(id)) continue;
            var top = Math.Max(Header, HourY(r.SessionStartHour)); var bottom = Math.Min(Height, HourY(r.SessionStartHour + r.SessionDuration));
            if (bottom <= top) continue;
            Rect(h, Column + Gutter, top + 1, Width - 3, bottom - 1, SWConsoleStyle.Selected);
            Rect(h, Column + Gutter, top + 1, Column + Gutter + 3, bottom - 1, r.SessionEmergency ? SWConsoleStyle.Bad : r.SessionConfirmed ? SWConsoleStyle.Good : SWConsoleStyle.Brass);
            if (bottom - top >= 19) Text(h, Column + Gutter + 7, top + 7, Short(r.SessionTitle, (int)((Width - Column - Gutter - 14) / 8)), SWConsoleStyle.TextBright);
        }
        if (DraftStartHour is { } start && DraftBillId != null) DrawDraft(h, start, DraftDuration, DraftValid, false);
        if (_press != null && _dragged && _pointer.X >= Column) DrawDraft(h, _targetStart, _targetDuration, ValidSpan(_targetStart, _targetDuration), true);
        if (_press != null && _dragged && _mode == 1) DrawBillGhost(h);
    }
    private void DrawBillGhost(DrawingHandleScreen h)
    {
        SWBillOption? bill = null;
        foreach (var item in _bills)
            if (item.Id == _dragBill) { bill = item; break; }
        if (bill == null) return;
        const float width = 200, height = 50, offset = 12;
        var x = _pointer.X + offset + width <= Width ? _pointer.X + offset : _pointer.X - offset - width;
        var y = _pointer.Y + offset + height <= Height ? _pointer.Y + offset : _pointer.Y - offset - height;
        Rect(h, x, y, x + width, y + height, SWConsoleStyle.Accent.WithAlpha(.8f));
        Rect(h, x + 1, y + 1, x + width - 1, y + height - 1, SWConsoleStyle.BgPanel.WithAlpha(.9f));
        Text(h, x + 8, y + 3, $"№{bill.Id}", SWConsoleStyle.Brass);
        Text(h, x + 8, y + 24, Short(bill.Name, 22), SWConsoleStyle.TextBright);
    }
    private void DrawDraft(DrawingHandleScreen h, int start, int duration, bool valid, bool preview)
    {
        var top = Math.Max(Header, HourY(start)); var bottom = Math.Min(Height, HourY(start + duration));
        if (bottom <= top) return;
        Rect(h, Column + Gutter + 4, top + 3, Width - 7, bottom - 3, valid ? SWConsoleStyle.Hover : SWConsoleStyle.PillBad);
        Rect(h, Column + Gutter + 4, top + 3, Column + Gutter + 7, bottom - 3, valid ? SWConsoleStyle.Accent : SWConsoleStyle.Bad);
        var billId = preview ? _dragBill : DraftBillId;
        var name = "Проект";
        foreach (var bill in _bills)
            if (bill.Id == billId) { name = bill.Name; break; }
        var label = $"{start:00}:00–{start + duration:00}:00  {name}";
        if (bottom - top >= 20) Text(h, Column + Gutter + 12, top + 8, Short(label, (int)((Width - Column - Gutter - 30) / 8)), valid ? SWConsoleStyle.TextBright : SWConsoleStyle.Bad);
        if (bottom - top >= 30) Rect(h, Width - 45, bottom - 8, Width - 13, bottom - 6, SWConsoleStyle.TextDim);
    }
    private static string Short(string text, int max) => max <= 0 ? string.Empty : text.Length <= max ? text : max <= 1 ? "…" : text.Substring(0, max - 1) + "…";
    private void Rect(DrawingHandleScreen h, float l, float t, float r, float b, Color c) { if (r > l && b > t) h.DrawRect(new UIBox2(l * UIScale, t * UIScale, r * UIScale, b * UIScale), c); }
    private void Text(DrawingHandleScreen h, float x, float y, string text, Color c) => h.DrawString(SWConsoleStyle.Small, new Vector2(x * UIScale, y * UIScale), text, UIScale, c);
}



