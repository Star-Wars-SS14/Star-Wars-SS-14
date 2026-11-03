using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared._SW._Senate;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Client._SW._Senate.Consoles.Statistics;

/// <summary>A searchable register that keeps the selection and document alive across unchanged updates.</summary>
public sealed class SWStatisticsDocuments : BoxContainer
{
    private readonly LineEdit _search;
    private readonly BoxContainer _rows;
    private readonly BoxContainer _document;
    private readonly Label _count;
    private readonly string _countNoun;
    private readonly ScrollContainer _listScroll;
    private readonly ScrollContainer _documentScroll;
    private readonly Dictionary<int, SWConsoleButton> _rowButtons = new();
    private Control? _revealRow;
    private List<SWConsoleDocumentEntry> _entries = new();
    private string? _shownRevision;
    private int? _shownId;
    private bool _initialized;
    public int? SelectedId { get; private set; }

    public SWStatisticsDocuments(string searchPlaceholder = "Поиск по реестру…", string countNoun = "Документов",
        float listWidth = 320, float listBreakpoint = 710)
    {
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        VerticalExpand = true;
        var navigation = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 8 };
        _countNoun = countNoun;
        _search = new LineEdit { Name = "DocumentSearch", PlaceHolder = searchPlaceholder, HorizontalExpand = true };
        _search.OnTextChanged += _ => RefreshRows();
        _count = SWConsoleStyle.Text(string.Empty, SWConsoleStyle.TextDim, SWConsoleStyle.Small);
        _rows = new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, SeparationOverride = 2 };
        _listScroll = new ScrollContainer { HScrollEnabled = false, MinHeight = 220, HorizontalExpand = true, VerticalExpand = true };
        _listScroll.AddChild(_rows);
        navigation.AddChild(_search);
        navigation.AddChild(_count);
        navigation.AddChild(_listScroll);
        _document = new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, SeparationOverride = 8 };
        _documentScroll = new ScrollContainer { HScrollEnabled = false, HorizontalExpand = true, VerticalExpand = true };
        _documentScroll.AddChild(_document);
        var dossier = new PanelContainer { HorizontalExpand = true, VerticalExpand = true, PanelOverride = new SWConsoleSurface
        {
            Background = SWConsoleStyle.BgPanel,
            ContentMarginLeftOverride = 12, ContentMarginRightOverride = 12,
            ContentMarginTopOverride = 12, ContentMarginBottomOverride = 12,
        } };
        dossier.AddChild(_documentScroll);
        AddChild(new SWConsoleColumns(navigation, dossier, listWidth, listBreakpoint, fixedFirst: false) { VerticalExpand = true });
    }

    public void ResetLocalState()
    {
        _search.SetText(string.Empty, invokeEvent: false);
        SelectedId = null;
        _shownId = null;
        _shownRevision = null;
        _revealRow = null;
        _initialized = false;
        _entries.Clear();
        _rows.RemoveAllChildren();
        _rowButtons.Clear();
        _document.RemoveAllChildren();
        _listScroll.VScroll = 0;
        _documentScroll.VScroll = 0;
        SetDocuments(Array.Empty<SWConsoleDocumentEntry>());
    }

    public void SetDocuments(IEnumerable<SWConsoleDocumentEntry> entries)
    {
        var next = entries.ToList();
        var rowsChanged = !_entries.Select(e => (e.Id, e.Title, e.Subtitle)).SequenceEqual(next.Select(e => (e.Id, e.Title, e.Subtitle)));
        _entries = next;
        var previousId = SelectedId;
        if (!_entries.Any(e => e.Id == SelectedId))
            SelectedId = _entries.FirstOrDefault()?.Id;
        if (rowsChanged || !_initialized)
            RefreshRows();
        else
            RefreshSelection(previousId);
        RefreshDocument();
        _initialized = true;
    }

    public void SelectDocument(int id, bool reveal = false)
    {
        if (!_entries.Any(e => e.Id == id))
            return;
        var previousId = SelectedId;
        SelectedId = id;
        _revealRow = null;
        if (reveal && _search.Text.Length > 0)
        {
            _search.SetText(string.Empty, invokeEvent: false);
            RefreshRows();
        }
        else
        {
            RefreshSelection(previousId);
        }
        if (reveal && _rowButtons.TryGetValue(id, out var row))
            _revealRow = row;
        RefreshDocument();
    }

    private void RefreshSelection(int? previousId)
    {
        if (previousId == SelectedId)
            return;
        if (previousId is { } previous && _rowButtons.TryGetValue(previous, out var oldRow))
            oldRow.SetAppearance(SWConsoleStyle.TextBright, quiet: true);
        if (SelectedId is { } selected && _rowButtons.TryGetValue(selected, out var newRow))
            newRow.SetAppearance(SWConsoleStyle.TextBright, selected: true, quiet: true);
    }

    private void RefreshRows()
    {
        _rows.RemoveAllChildren();
        _rowButtons.Clear();
        _revealRow = null;
        var visible = _entries.Where(e => (e.Title + " " + e.Subtitle).Contains(_search.Text, StringComparison.OrdinalIgnoreCase)).ToList();
        _count.Text = $"{_countNoun}: {visible.Count} из {_entries.Count}";
        foreach (var entry in visible)
        {
            var button = new SWConsoleButton { Name = $"DocumentEntry_{entry.Id}", HorizontalExpand = true };
            button.Label.Visible = false;
            button.SetAppearance(SWConsoleStyle.TextBright, selected: entry.Id == SelectedId, quiet: true);
            var text = new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, SeparationOverride = 4 };
            text.AddChild(SWStatisticsText.Wrap(entry.Title, SWConsoleStyle.TextBright));
            text.AddChild(SWStatisticsText.Wrap(entry.Subtitle, SWConsoleStyle.TextDim));
            button.AddChild(text);
            button.OnPressed += _ => SelectDocument(entry.Id);
            _rows.AddChild(button);
            _rowButtons[entry.Id] = button;
        }
        if (visible.Count == 0)
            _rows.AddChild(SWStatisticsText.Wrap(_entries.Count == 0 ? "Реестр пуст." : "Ничего не найдено.", SWConsoleStyle.TextDim));
    }

    private void RefreshDocument()
    {
        var entry = _entries.FirstOrDefault(e => e.Id == SelectedId);
        if (_initialized && entry?.Id == _shownId && entry?.Revision == _shownRevision)
            return;
        if (_shownId != entry?.Id)
            _documentScroll.VScroll = 0;
        _shownId = entry?.Id;
        _shownRevision = entry?.Revision;
        _document.RemoveAllChildren();
        _document.AddChild(entry == null ? SWStatisticsText.Wrap("Документ не выбран", SWConsoleStyle.TextDim) : entry.Build());
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (_revealRow == null || _revealRow.Height <= 0)
            return;

        _listScroll.VScroll = _revealRow.Position.Y;
        _revealRow = null;
    }
}
