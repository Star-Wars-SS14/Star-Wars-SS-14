using System;
using System.Linq;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Ministries;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._SW._Senate.Consoles.MinistryWork;

/// <summary>Bounded journal with a persistent filter and paged, scrollable entries.</summary>
public sealed class SWJusticeJournal : BoxContainer
{
    private const int PageSize = 20;
    private readonly LineEdit _filter = new() { Name = "JusticeJournalFilter", PlaceHolder = "Номер, название дела или мир", HorizontalExpand = true };
    private readonly BoxContainer _rows = new() { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, SeparationOverride = 8 };
    private readonly ScrollContainer _scroll = new() { Name = "JusticeJournalScroll", HScrollEnabled = false, HorizontalExpand = true, VerticalExpand = true };
    private readonly Label _pageLabel = SWConsoleStyle.Text("", SWConsoleStyle.TextDim, SWConsoleStyle.Small);
    private readonly Button _previous = SWConsoleStyle.TabButton("Назад");
    private readonly Button _next = SWConsoleStyle.TabButton("Далее");
    private SWJusticeInspectionState? _state;
    private string? _revision;
    private int _page;
    public event Action<int, bool>? CaseSelected;

    public SWJusticeJournal()
    {
        Orientation = LayoutOrientation.Vertical; HorizontalExpand = VerticalExpand = true;
        AddChild(_filter);
        _filter.OnTextChanged += _ => { _page = 0; _scroll.VScroll = 0; Refresh(); };
        var navigation = new WrapContainer { HorizontalExpand = true, SeparationOverride = 6, CrossSeparationOverride = 4 };
        _previous.OnPressed += _ => { _page--; _scroll.VScroll = 0; Refresh(); };
        _next.OnPressed += _ => { _page++; _scroll.VScroll = 0; Refresh(); };
        navigation.AddChild(_previous); navigation.AddChild(_pageLabel); navigation.AddChild(_next);
        AddChild(navigation);
        _scroll.AddChild(_rows); AddChild(_scroll);
    }
    public void SetFilter(string filter) { _filter.SetText(filter, invokeEvent: true); }
    public void SetState(SWJusticeInspectionState? state)
    {
        _state = state;
        var revision = state == null ? "" : string.Join("|", state.Journal.Select(e => $"{e.Sequence}:{e.CaseId}:{e.Day}:{e.Hour}:{e.Minute}:{e.CaseTitle}:{e.InstitutionName}:{e.WorldName}:{e.Message}")) + "/" +
            string.Join(",", state.ActiveCases.Select(c => c.Id)) + "/" + string.Join(",", state.Archive.Select(c => c.Id));
        if (_revision == revision) return;
        _revision = revision; Refresh();
    }
    private void Refresh()
    {
        var scroll = _scroll.VScroll;
        var query = _filter.Text.Trim();
        var entries = _state?.Journal.Where(e => query.Length == 0 || $"{e.CaseId} {e.CaseTitle} {e.InstitutionName} {e.WorldName}".Contains(query, StringComparison.OrdinalIgnoreCase)).ToList() ?? new();
        var pages = Math.Max(1, (entries.Count + PageSize - 1) / PageSize);
        _page = Math.Clamp(_page, 0, pages - 1);
        _previous.Disabled = _page == 0; _next.Disabled = _page == pages - 1;
        _pageLabel.Text = $"{_page + 1} / {pages}   Записей: {entries.Count}";
        _rows.RemoveAllChildren();
        if (entries.Count == 0) _rows.AddChild(SWConsoleStyle.Wrap("Записей нет", SWConsoleStyle.TextDim));
        foreach (var entry in entries.Skip(_page * PageSize).Take(PageSize))
        {
            _rows.AddChild(SWConsoleStyle.Wrap($"День {entry.Day}, {entry.Hour:00}:{entry.Minute:00}\nДело №{entry.CaseId}: {entry.CaseTitle}\n{entry.InstitutionName}, {entry.WorldName}\n{entry.Message}", SWConsoleStyle.TextBright));
            var active = _state?.ActiveCases.Any(c => c.Id == entry.CaseId) == true;
            var archived = _state?.Archive.Any(c => c.Id == entry.CaseId) == true;
            var open = SWConsoleStyle.TabButton(archived ? "Открыть архивное дело" : "Открыть дело");
            open.Disabled = !active && !archived;
            if (!open.Disabled) open.OnPressed += _ => CaseSelected?.Invoke(entry.CaseId, archived);
            _rows.AddChild(open);
        }
        _scroll.VScroll = scroll;
    }
}
