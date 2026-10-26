using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared._SW._Senate;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Client._SW._Senate;

public sealed record SWConsoleDocumentEntry(int Id, string Title, string Subtitle, string Revision, Func<Control> Build);

/// <summary>A searchable register that keeps the selection and document alive across unchanged updates.</summary>
public sealed class SWConsoleDocuments : BoxContainer
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

    public SWConsoleDocuments(string searchPlaceholder = "Поиск по реестру…", string countNoun = "Документов",
        float listWidth = 200, float listBreakpoint = 710)
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
        AddChild(new SWConsoleColumns(navigation, _documentScroll, listWidth, listBreakpoint, fixedFirst: true) { VerticalExpand = true });
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
            text.AddChild(SWConsoleStyle.Wrap(entry.Title, SWConsoleStyle.TextBright));
            text.AddChild(SWConsoleStyle.Wrap(entry.Subtitle, SWConsoleStyle.TextDim));
            button.AddChild(text);
            button.OnPressed += _ => SelectDocument(entry.Id);
            _rows.AddChild(button);
            _rowButtons[entry.Id] = button;
        }
        if (visible.Count == 0)
            _rows.AddChild(SWConsoleStyle.Wrap(_entries.Count == 0 ? "Реестр пуст." : "Ничего не найдено.", SWConsoleStyle.TextDim));
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
        _document.AddChild(entry == null ? SWConsoleStyle.Wrap("Документ не выбран", SWConsoleStyle.TextDim) : entry.Build());
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

public static class SWConsoleDocument
{
    public static BoxContainer Body(string title, string description)
    {
        var body = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, SeparationOverride = 10 };
        var heading = SWConsoleStyle.Wrap(title, SWConsoleStyle.TextBright);
        heading.Stylesheet = new Stylesheet(new StyleRule[]
        {
            StylesheetHelpers.Element<RichTextLabel>().Prop("font", SWConsoleStyle.Title),
        });
        body.AddChild(heading);
        if (!string.IsNullOrWhiteSpace(description))
            body.AddChild(SWConsoleStyle.Wrap(description, SWConsoleStyle.TextBright));
        return body;
    }

    public static BoxContainer Metadata(params (string Label, string Value)[] fields)
    {
        var box = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, SeparationOverride = 6 };
        foreach (var (label, value) in fields)
        {
            box.AddChild(SWConsoleStyle.Text(label, SWConsoleStyle.TextDim, SWConsoleStyle.Small));
            box.AddChild(SWConsoleStyle.Wrap(value, SWConsoleStyle.TextBright));
        }
        return box;
    }

    public static void Effects(BoxContainer target, IReadOnlyList<SWBillEffectEntry> effects, string heading = "Последствия")
    {
        if (effects.Count == 0)
            return;
        target.AddChild(SWConsoleStyle.Section(heading));
        foreach (var effect in effects)
            target.AddChild(SWConsoleStyle.Wrap($"{effect.StatName}   {effect.Delta:+0;-0}", effect.Color));
    }

    public static Control Bill(SWBillEntry bill, Control? actions = null)
    {
        var body = Body(bill.Name, bill.Description);
        var metadata = Metadata(("Документ", $"№ {bill.Id}"),
            (bill.AmendsLawId != null ? "Автор поправок" : "Автор", bill.Author), ("Состояние", bill.Stage));
        if (bill.AmendsLawId is { } lawId)
        {
            body.AddChild(SWConsoleStyle.Section("К закону"));
            body.AddChild(SWConsoleStyle.Wrap($"№ {lawId}: {bill.AmendsLawName}", SWConsoleStyle.Accent));
        }
        Effects(metadata, bill.Effects, bill.AmendsLawId != null ? "Изменения к закону" :
            bill.CanAmend ? "Действующие последствия" : "Последствия");
        var document = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, SeparationOverride = 16 };
        document.AddChild(new SWConsoleColumns(body, metadata));
        if (actions != null)
            document.AddChild(actions);
        if (bill.Amendments.Count > 0)
        {
            document.AddChild(SWConsoleStyle.Section("Принятые поправки"));
            foreach (var amendment in bill.Amendments)
            {
                var entry = Body($"№ {amendment.Id}: {amendment.Name}", amendment.Description);
                entry.AddChild(SWConsoleStyle.Wrap($"Автор: {amendment.Author}", SWConsoleStyle.TextDim));
                Effects(entry, amendment.Effects, "Изменения к закону");
                document.AddChild(entry);
            }
        }
        return document;
    }

    // Include content, not only ids: an edited bill must not keep a stale preview.
    public static string Revision(SWBillEntry bill) =>
        $"{bill.Name}\n{bill.Author}\n{bill.Stage}\n{bill.Description}\n{bill.AmendsLawId}:{bill.AmendsLawName}:{bill.CanAmend}\n" +
        string.Join(";", bill.Effects.Select(e => $"{e.StatName}:{e.Delta}:{e.Color}")) + "\n" +
        string.Join(";", bill.EffectValues.OrderBy(e => e.Key).Select(e => $"{e.Key}:{e.Value}")) + "\n" +
        string.Join("\n", bill.Amendments.Select(amendment => $"{amendment.Id}:{Revision(amendment)}"));

    public static Control CourtCase(SWCourtCaseEntry entry)
    {
        var body = Body(entry.Title, entry.Description);
        if (entry.Hints.Count > 0)
        {
            body.AddChild(SWConsoleStyle.Section("Позиции сторон"));
            foreach (var hint in entry.Hints)
                body.AddChild(SWConsoleStyle.Wrap(hint, SWConsoleStyle.TextBright));
        }
        if (entry.OutcomePlaintiff.Length > 0)
            body.AddChild(SWConsoleStyle.Wrap($"В пользу истца: {entry.OutcomePlaintiff}", SWConsoleStyle.Good));
        if (entry.OutcomeDefendant.Length > 0)
            body.AddChild(SWConsoleStyle.Wrap($"В пользу ответчика: {entry.OutcomeDefendant}", SWConsoleStyle.Warn));
        if (entry.Verdict.Length > 0)
        {
            body.AddChild(SWConsoleStyle.Section("Вердикт"));
            body.AddChild(SWConsoleStyle.Wrap(entry.Verdict, SWConsoleStyle.Accent));
        }
        return new SWConsoleColumns(body, CourtMetadata(entry));
    }

    public static BoxContainer CourtMetadata(SWCourtCaseEntry entry) => Metadata(
        ("Дело", entry.Number), ("Категория", entry.TypeLabel), ("Стороны", entry.Parties),
        ("Слушание", entry.HearingLabel), ("Председатель", entry.Presiding), ("Состояние", entry.StatusLabel));

    public static string Revision(SWCourtCaseEntry entry) =>
        $"{entry.Title}|{entry.Number}|{entry.TypeLabel}|{entry.Parties}|{entry.Description}|{entry.HearingLabel}|{entry.Presiding}|{entry.StatusLabel}|{entry.Verdict}|{entry.OutcomePlaintiff}|{entry.OutcomeDefendant}|" + string.Join(";", entry.Hints);
}
