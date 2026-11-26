using System;
using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;
using static Robust.Client.UserInterface.Controls.BoxContainer;

namespace Content.Client._SW._Senate.Training;

/// <summary>Two parts side by side when the width allows, one under the other otherwise, so nothing is cut off.</summary>
public sealed class SWTrainingAdaptiveRow : Container
{
    private readonly Control _first;
    private readonly Control _second;
    private readonly float _breakpoint;
    private readonly float _firstShare;
    private readonly float _gap;

    public SWTrainingAdaptiveRow(Control first, Control second, float breakpoint, float firstShare = 0.5f, float gap = 6f)
    {
        _first = first;
        _second = second;
        _breakpoint = breakpoint;
        _firstShare = firstShare;
        _gap = gap;
        HorizontalExpand = true;
        AddChild(first);
        AddChild(second);
    }

    protected override void Resized()
    {
        base.Resized();
        InvalidateMeasure();
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var width = float.IsFinite(availableSize.X) ? availableSize.X : Width > 1f ? Width : _breakpoint;
        if (width < _breakpoint)
        {
            _first.Measure(new Vector2(width, float.PositiveInfinity));
            _second.Measure(new Vector2(width, float.PositiveInfinity));
            return new Vector2(width, _first.DesiredSize.Y + _gap + _second.DesiredSize.Y);
        }

        var firstWidth = (width - _gap) * _firstShare;
        _first.Measure(new Vector2(firstWidth, float.PositiveInfinity));
        _second.Measure(new Vector2(width - _gap - firstWidth, float.PositiveInfinity));
        return new Vector2(width, Math.Max(_first.DesiredSize.Y, _second.DesiredSize.Y));
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        if (finalSize.X < _breakpoint)
        {
            var firstHeight = _first.DesiredSize.Y;
            _first.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(finalSize.X, firstHeight)));
            _second.Arrange(UIBox2.FromDimensions(new Vector2(0f, firstHeight + _gap),
                new Vector2(finalSize.X, Math.Max(0f, finalSize.Y - firstHeight - _gap))));
            return finalSize;
        }

        var firstWidth = (finalSize.X - _gap) * _firstShare;
        _first.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(firstWidth, finalSize.Y)));
        _second.Arrange(UIBox2.FromDimensions(new Vector2(firstWidth + _gap, 0f),
            new Vector2(Math.Max(0f, finalSize.X - firstWidth - _gap), finalSize.Y)));
        return finalSize;
    }
}

/// <summary>
/// Builds teaching illustrations of game windows from the same style helpers and labels. They are visibly marked as
/// illustrations, their order follows the XAML but their geometry is not claimed to match, their controls take no
/// input and have no handlers, so nothing here can reach the server. Budget illustrations use two columns when the
/// width allows and one otherwise, so the control a stage acts on is never out of reach.
/// </summary>
public static class SWTrainingUiPanels
{
    private const int ExampleDayBudget = 100000;
    private const string FirstMinistry = "Министерство обороны";
    private const string SecondMinistry = "Министерство финансов";
    private const string FirstSector = "Корусант";
    private const string SecondSector = "Тайтон";

    /// <summary>Below this width a budget illustration is one column; inside a column, field pairs stack the same way.</summary>
    private const float ColumnsBreakpoint = 600f;
    private const float RowBreakpoint = 296f;
    private const float FieldFirstShare = 0.58f;
    private const float ShareRowFirstShare = 0.49f;

    public static float MaxWidth(SWTrainingPanelState state) => state is SWTrainingFinancePanel or SWTrainingChancellorPanel ? 660f : 480f;

    public static float MinWidth(SWTrainingPanelState state) => state is SWTrainingFinancePanel or SWTrainingChancellorPanel ? 640f : 380f;

    /// <summary>The control a stage acts on, in words, for layouts where no pointer can be drawn on it.</summary>
    public static string? TargetLabel(SWTrainingPanelState state) => state switch
    {
        SWTrainingShiftPanel { Recognized: false } => null,
        SWTrainingShiftPanel { OnShift: true } => "кнопка «Сдать смену»",
        SWTrainingShiftPanel => "кнопка «Заступить на смену»",
        SWTrainingFinancePanel { AimAtSubmit: true } => "кнопка «Подать проект канцлеру»",
        SWTrainingFinancePanel { ShowSubmit: true } => "поле «Сектора, %»",
        SWTrainingChancellorPanel { RejectText: not null } => "кнопка «Отклонить»",
        SWTrainingChancellorPanel { Phase: SWTrainingBudgetPhase.Pending } => "кнопка «Принять бюджет»",
        SWTrainingFormPanel form => FormTargetLabel(form),
        _ => null,
    };

    private static string? FormTargetLabel(SWTrainingFormPanel form)
    {
        foreach (var row in form.Rows)
        {
            if (!row.Target)
                continue;

            return row.Kind switch
            {
                SWTrainingRowKind.Buttons when row.Buttons != null && row.TargetIndex >= 0 => $"кнопка «{row.Buttons[row.TargetIndex]}»",
                SWTrainingRowKind.Field => $"поле «{row.Text}»",
                SWTrainingRowKind.Options => row.Text.Length > 0 ? $"список «{row.Text}»" : "список",
                SWTrainingRowKind.Check => $"флажок «{row.Text}»",
                SWTrainingRowKind.Counter when row.Buttons != null && row.TargetIndex >= 0 => $"кнопка «{row.Buttons[row.TargetIndex]}» у строки «{row.Text}»",
                SWTrainingRowKind.Item => $"строка «{row.Text}»",
                _ => $"кнопка «{row.Text}»",
            };
        }

        return null;
    }

    /// <param name="markTarget">Frames the target inside the controls, where the stage layer cannot outline it.</param>
    public static Control Build(SWTrainingPanelState state, out Control? target, bool markTarget = false)
    {
        target = null;
        var root = new PanelContainer { HorizontalExpand = true, MouseFilter = Control.MouseFilterMode.Ignore };
        SWConsoleStyle.StyleRoot(root);
        var box = new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, SeparationOverride = 4, Margin = new Thickness(10, 6, 10, 8) };
        root.AddChild(box);

        switch (state)
        {
            case SWTrainingShiftPanel shift:
                BuildShift(box, shift, markTarget, out target);
                break;
            case SWTrainingFinancePanel finance:
                BuildFinance(box, finance, markTarget, out target);
                break;
            case SWTrainingChancellorPanel chancellor:
                BuildChancellor(box, chancellor, markTarget, out target);
                break;
            case SWTrainingBudgetStatusPanel status:
                BuildBudgetStatus(box, status);
                break;
            case SWTrainingFormPanel form:
                BuildForm(box, form, markTarget, out target);
                break;
        }

        return root;
    }

    private static Control Slot(Control control, bool mark)
    {
        if (!mark)
            return control;

        var frame = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.Transparent,
                BorderColor = SWConsoleStyle.Accent,
                BorderThickness = new Thickness(2),
            },
        };
        frame.AddChild(control);
        return frame;
    }

    private static Button InertButton(string text, Color accent)
    {
        var button = SWConsoleStyle.Action(text, accent);
        button.MouseFilter = Control.MouseFilterMode.Ignore;
        return button;
    }

    private static LineEdit InertField(string text) => new()
    {
        Text = text,
        MinWidth = 45,
        Editable = false,
        MouseFilter = Control.MouseFilterMode.Ignore,
    };

    private static BoxContainer Column() => new()
    {
        Orientation = LayoutOrientation.Vertical,
        HorizontalExpand = true,
        SeparationOverride = 4,
    };

    private static BoxContainer LabeledField(string label, Color color, Control field)
    {
        var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 6 };
        row.AddChild(SWConsoleStyle.Text(label, color, SWConsoleStyle.Body));
        row.AddChild(field);
        return row;
    }

    private static void BuildShift(BoxContainer box, SWTrainingShiftPanel state, bool mark, out Control? target)
    {
        target = null;
        box.AddChild(SWConsoleStyle.Text("Смена персонала", SWConsoleStyle.Brass, SWConsoleStyle.Strong));

        var header = new PanelContainer { HorizontalExpand = true };
        SWConsoleStyle.StyleHeader(header);
        var headerRow = new BoxContainer { Orientation = LayoutOrientation.Horizontal, Margin = new Thickness(14, 10, 14, 10), SeparationOverride = 0 };
        headerRow.AddChild(new SWConsoleSeal { Margin = new Thickness(0, 0, 12, 0) });
        var headerText = new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, VerticalAlignment = Control.VAlignment.Center, SeparationOverride = 2 };
        headerText.AddChild(SWConsoleStyle.Text("РЕСПУБЛИКА / ГОЛОНЕТ", SWConsoleStyle.Brass, SWConsoleStyle.Small));
        headerText.AddChild(SWConsoleStyle.Text("Дежурства персонала", SWConsoleStyle.TextBright, SWConsoleStyle.Header));
        headerRow.AddChild(headerText);
        header.AddChild(headerRow);
        box.AddChild(header);

        box.AddChild(SWConsoleStyle.Wrap(state.Recognized
            ? $"Сотрудник: {state.Name}, должность: Сенатский врач"
            : $"Сотрудник: {state.Name}, должность не сенатского персонала", SWConsoleStyle.TextBright));

        // As in the game, the notice is green while on shift and red otherwise.
        if (state.Notice.Length > 0)
            box.AddChild(SWConsoleStyle.Text(state.Notice, state.OnShift ? SWConsoleStyle.Good : SWConsoleStyle.Bad, SWConsoleStyle.Small));

        if (state.Recognized)
        {
            var toggle = state.OnShift
                ? InertButton("Сдать смену", SWConsoleStyle.Bad)
                : InertButton("Заступить на смену", SWConsoleStyle.Good);
            var slot = Slot(toggle, mark);
            slot.HorizontalAlignment = Control.HAlignment.Left;
            box.AddChild(slot);
            target = toggle;
        }

        box.AddChild(SWConsoleStyle.Text("На смене:", SWConsoleStyle.TextBright, SWConsoleStyle.Strong));
        if (state.OnShift)
        {
            box.AddChild(SWConsoleStyle.Section("Сенатский врач"));
            var row = SWConsoleStyle.Row(SWConsoleStyle.BgPanel);
            row.AddChild(SWConsoleStyle.Text(state.Name, SWConsoleStyle.TextBright, SWConsoleStyle.Body));
            box.AddChild(row);
        }
        else
        {
            box.AddChild(SWConsoleStyle.Text("Никто не на смене.", SWConsoleStyle.TextBright, SWConsoleStyle.Small));
        }
    }

    private static void BuildFinance(BoxContainer box, SWTrainingFinancePanel state, bool mark, out Control? target)
    {
        target = null;
        box.AddChild(SWConsoleStyle.Text("Бюджеты", SWConsoleStyle.Brass, SWConsoleStyle.Strong));
        var left = Column();
        var right = Column();
        box.AddChild(new SWTrainingAdaptiveRow(left, right, ColumnsBreakpoint, gap: 12f));

        var info = $"{state.Phase}   Бюджет дня: {ExampleDayBudget:N0} кр";
        if (state.RejectReason != null)
            info += $"\nЗамечание канцлера: {state.RejectReason}";
        left.AddChild(SWConsoleStyle.Wrap(info, SWConsoleStyle.TextBright));

        if (state.Stale)
        {
            left.AddChild(SWConsoleStyle.Wrap("Проект изменился на сервере. Загрузите актуальную версию перед отправкой; местные правки будут сброшены.", SWConsoleStyle.Bad));
            var reload = InertButton("Загрузить актуальный проект", SWConsoleStyle.Neutral);
            reload.HorizontalAlignment = Control.HAlignment.Left;
            left.AddChild(reload);
        }

        left.AddChild(SWConsoleStyle.Wrap(
            $"Сектора {state.Sector}% — {Pool(state.Sector):N0} кр\nВедомства {state.Ministry}% — {Pool(state.Ministry):N0} кр\nФонд Республики {state.Republic}% — {Pool(state.Republic):N0} кр\nРезерв {state.Reserve}% — {Pool(state.Reserve):N0} кр",
            SWConsoleStyle.TextBright));

        var bar = new BoxContainer { Orientation = LayoutOrientation.Horizontal, HorizontalExpand = true, MinHeight = 10 };
        bar.AddChild(Segment(SWConsoleStyle.Accent, state.Sector));
        bar.AddChild(Segment(SWConsoleStyle.Good, state.Ministry));
        bar.AddChild(Segment(SWConsoleStyle.TextBright, state.Republic));
        bar.AddChild(Segment(SWConsoleStyle.Brass, state.Reserve));
        left.AddChild(bar);

        var sectorField = InertField(state.Sector.ToString());
        var aimAtField = state.ShowSubmit && !state.AimAtSubmit && !state.Stale && state.RejectReason == null;
        left.AddChild(new SWTrainingAdaptiveRow(
            LabeledField("Сектора, %", SWConsoleStyle.Accent, Slot(sectorField, mark && aimAtField)),
            LabeledField("Ведомства, %", SWConsoleStyle.Good, InertField(state.Ministry.ToString())),
            RowBreakpoint, FieldFirstShare));
        if (aimAtField)
            target = sectorField;

        left.AddChild(new SWTrainingAdaptiveRow(
            LabeledField("Фонд Республики, %", SWConsoleStyle.TextBright, InertField(state.Republic.ToString())),
            LabeledField("Резерв, %", SWConsoleStyle.Brass, InertField(state.Reserve.ToString())),
            RowBreakpoint, FieldFirstShare));

        right.AddChild(SWConsoleStyle.Section("Доли ведомств"));
        var share = Pool(state.Ministry) / 2;
        right.AddChild(ShareRow(FirstMinistry, share));
        right.AddChild(ShareRow(SecondMinistry, share));

        if (!state.ShowSubmit)
            return;

        var submit = InertButton("Подать проект канцлеру", SWConsoleStyle.Good);
        var slot = Slot(submit, mark && state.AimAtSubmit);
        slot.HorizontalAlignment = Control.HAlignment.Left;
        right.AddChild(slot);
        if (state.AimAtSubmit)
            target = submit;
    }

    private static Control ShareRow(string ministry, int amount)
    {
        var weight = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 6 };
        weight.AddChild(SWConsoleStyle.Text("Вес", SWConsoleStyle.TextDim, SWConsoleStyle.Small));
        weight.AddChild(InertField("1"));
        weight.AddChild(SWConsoleStyle.Text($"≈ {amount:N0} кр", SWConsoleStyle.TextBright, SWConsoleStyle.Body));
        return new SWTrainingAdaptiveRow(SWConsoleStyle.Wrap(ministry, SWConsoleStyle.TextBright), weight, RowBreakpoint, ShareRowFirstShare);
    }

    private static void BuildChancellor(BoxContainer box, SWTrainingChancellorPanel state, bool mark, out Control? target)
    {
        target = null;
        var appRow = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 10 };
        appRow.AddChild(SWConsoleStyle.Text("Финансы", SWConsoleStyle.Brass, SWConsoleStyle.Strong));
        appRow.AddChild(SWConsoleStyle.Text("Бюджет", SWConsoleStyle.TextBright, SWConsoleStyle.Strong));
        box.AddChild(appRow);

        var left = Column();
        var right = Column();
        box.AddChild(new SWTrainingAdaptiveRow(left, right, ColumnsBreakpoint, gap: 12f));

        var approved = state.Phase == SWTrainingBudgetPhase.Approved;
        var phaseText = approved ? "[одобрен]" : state.Phase == SWTrainingBudgetPhase.Pending ? "[на одобрении]" : "[проект не подан]";
        var republic = Pool(state.Republic);
        var remainder = approved ? $" (остаток {republic:N0} кр)" : string.Empty;
        var summary = $"Бюджет дня: {ExampleDayBudget:N0} кр.   Сектора: {Pool(state.Sector):N0} кр   Фонд Республики: {republic:N0} кр{remainder}   Ведомства: {Pool(state.Ministry):N0} кр   Республиканский резерв: {Pool(state.Reserve):N0} кр   {phaseText}";
        if (state.AutoReason != null)
            summary += $"\nСтандартное распределение: {state.AutoReason}";
        left.AddChild(SWConsoleStyle.Wrap(summary, SWConsoleStyle.TextBright));

        if (state.Phase == SWTrainingBudgetPhase.NotSubmitted)
        {
            right.AddChild(SWConsoleStyle.Wrap("Минфин ещё не подал проект бюджета на сегодня.", SWConsoleStyle.TextDim));
            return;
        }

        left.AddChild(SWConsoleStyle.Section("ДОЛИ МИНИСТЕРСТВ (МИНФИН)"));
        var share = Pool(state.Ministry) / 2;
        left.AddChild(AmountRow(FirstMinistry, share));
        left.AddChild(AmountRow(SecondMinistry, share));

        var sectorShare = Pool(state.Sector) / 2;
        if (approved)
        {
            right.AddChild(SWConsoleStyle.Section("СЕКТОРА (ОДОБРЕНО)"));
            right.AddChild(AmountRow(FirstSector, sectorShare));
            right.AddChild(AmountRow(SecondSector, sectorShare));
            return;
        }

        right.AddChild(SWConsoleStyle.Wrap($"РАСПРЕДЕЛЕНИЕ СЕКТОРОВ (пул {Pool(state.Sector)} кр)", SWConsoleStyle.TextBright));
        var even = InertButton("Распределить поровну", SWConsoleStyle.Accent);
        even.HorizontalAlignment = Control.HAlignment.Left;
        right.AddChild(even);
        right.AddChild(SWConsoleStyle.Wrap("Дополнительно в фонд Республики: 0 кр", SWConsoleStyle.Brass));
        right.AddChild(SectorRow(FirstSector, sectorShare));
        right.AddChild(SectorRow(SecondSector, sectorShare));

        var approve = InertButton("Принять бюджет", SWConsoleStyle.Good);
        approve.HorizontalExpand = true;
        var aimAtReject = state.RejectText != null;
        right.AddChild(Slot(approve, mark && !aimAtReject));
        target = approve;

        var rejectRow = new BoxContainer { Orientation = LayoutOrientation.Horizontal, HorizontalExpand = true, SeparationOverride = 6 };
        rejectRow.AddChild(new LineEdit
        {
            PlaceHolder = "Замечание для Минфина",
            Text = state.RejectText ?? string.Empty,
            HorizontalExpand = true,
            Editable = false,
            MouseFilter = Control.MouseFilterMode.Ignore,
        });
        var reject = InertButton("Отклонить", SWConsoleStyle.Bad);
        rejectRow.AddChild(Slot(reject, mark && aimAtReject));
        right.AddChild(rejectRow);
        if (aimAtReject)
            target = reject;
    }

    private static Color AccentColor(SWTrainingAccent accent) => accent switch
    {
        SWTrainingAccent.Good => SWConsoleStyle.Good,
        SWTrainingAccent.Bad => SWConsoleStyle.Bad,
        SWTrainingAccent.Neutral => SWConsoleStyle.Neutral,
        _ => SWConsoleStyle.Accent,
    };

    private static void BuildForm(BoxContainer box, SWTrainingFormPanel state, bool mark, out Control? target)
    {
        target = null;
        if (state.Title.Length > 0)
            box.AddChild(SWConsoleStyle.Wrap(state.Title, SWConsoleStyle.Brass));

        foreach (var row in state.Rows)
        {
            switch (row.Kind)
            {
                case SWTrainingRowKind.Section:
                    box.AddChild(SWConsoleStyle.Wrap(row.Text, SWConsoleStyle.Brass));
                    break;
                case SWTrainingRowKind.Text:
                    box.AddChild(SWConsoleStyle.Wrap(row.Text, SWConsoleStyle.TextBright));
                    break;
                case SWTrainingRowKind.Dim:
                    if (row.Text.Length > 0)
                        box.AddChild(SWConsoleStyle.Wrap(row.Text, SWConsoleStyle.TextDim));
                    break;
                case SWTrainingRowKind.Good:
                    box.AddChild(SWConsoleStyle.Wrap(row.Text, SWConsoleStyle.Good));
                    break;
                case SWTrainingRowKind.Warn:
                    box.AddChild(SWConsoleStyle.Wrap(row.Text, SWConsoleStyle.Warn));
                    break;
                case SWTrainingRowKind.Bad:
                    box.AddChild(SWConsoleStyle.Wrap(row.Text, SWConsoleStyle.Bad));
                    break;
                case SWTrainingRowKind.Button:
                {
                    var button = InertButton(row.Text, AccentColor(row.Accent));
                    button.Disabled = row.Disabled;
                    var slot = Slot(button, mark && row.Target);
                    slot.HorizontalAlignment = Control.HAlignment.Left;
                    box.AddChild(slot);
                    if (row.Target)
                        target = button;
                    break;
                }
                case SWTrainingRowKind.Buttons:
                    box.AddChild(BuildChoice(row, mark, ref target));
                    break;
                case SWTrainingRowKind.Options:
                {
                    var options = new OptionButton { HorizontalExpand = true, MouseFilter = Control.MouseFilterMode.Ignore };
                    var items = row.Buttons ?? Array.Empty<string>();
                    for (var i = 0; i < items.Length; i++)
                        options.AddItem(items[i], i);
                    if (row.Current >= 0)
                        options.SelectId(row.Current);

                    var line = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 12, HorizontalExpand = true };
                    if (row.Text.Length > 0)
                        line.AddChild(SWConsoleStyle.Text(row.Text, SWConsoleStyle.TextDim, SWConsoleStyle.Body));
                    line.AddChild(Slot(options, mark && row.Target));
                    box.AddChild(line);
                    if (row.Target)
                        target = options;

                    // The real list opens as a popup; here its items are shown in place, so the choice stays readable.
                    if (row.Chosen)
                    {
                        for (var i = 0; i < items.Length; i++)
                            box.AddChild(SWConsoleStyle.Wrap(items[i], i == row.Current ? SWConsoleStyle.Accent : SWConsoleStyle.TextDim));
                    }

                    break;
                }
                case SWTrainingRowKind.Check:
                {
                    var check = new CheckBox { Text = row.Text, Pressed = row.Chosen, MouseFilter = Control.MouseFilterMode.Ignore };
                    box.AddChild(Slot(check, mark && row.Target));
                    if (row.Target)
                        target = check;
                    break;
                }
                case SWTrainingRowKind.Counter:
                {
                    var counter = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 4, HorizontalExpand = true };
                    var caption = SWConsoleStyle.Wrap(row.Text, SWConsoleStyle.TextBright);
                    caption.HorizontalExpand = true;
                    counter.AddChild(caption);
                    counter.AddChild(SWConsoleStyle.Text(row.Value ?? string.Empty, SWConsoleStyle.Accent, SWConsoleStyle.Strong));
                    var labels = row.Buttons ?? Array.Empty<string>();
                    for (var i = 0; i < labels.Length; i++)
                    {
                        var step = InertButton(labels[i], i == 0 ? SWConsoleStyle.Bad : SWConsoleStyle.Good);
                        step.Disabled = i == 0 ? row.Current == 0 : row.Disabled;
                        counter.AddChild(Slot(step, mark && i == row.TargetIndex));
                        if (i == row.TargetIndex)
                            target = step;
                    }

                    box.AddChild(counter);
                    break;
                }
                case SWTrainingRowKind.Field:
                {
                    var field = InertField(row.Value ?? string.Empty);
                    field.HorizontalExpand = true;
                    box.AddChild(SWConsoleStyle.Wrap(row.Text, SWConsoleStyle.TextDim));
                    box.AddChild(Slot(field, mark && row.Target));
                    if (row.Target)
                        target = field;
                    break;
                }
                case SWTrainingRowKind.Item:
                {
                    var item = SWConsoleStyle.Row(row.Chosen ? SWConsoleStyle.Selected : SWConsoleStyle.BgPanel);
                    var lines = new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true };
                    lines.AddChild(SWConsoleStyle.Wrap(row.Text, SWConsoleStyle.TextBright));
                    lines.AddChild(SWConsoleStyle.Wrap(row.Value ?? string.Empty, SWConsoleStyle.TextDim));
                    item.AddChild(lines);
                    box.AddChild(Slot(item, mark && row.Target));
                    if (row.Target)
                        target = item;
                    break;
                }
                case SWTrainingRowKind.Bar:
                    box.AddChild(SWConsoleStyle.Bar(row.Amount, 0f, 1f, AccentColor(row.Accent)));
                    break;
            }
        }
    }

    private static Control BuildChoice(SWTrainingRow row, bool mark, ref Control? target)
    {
        var wrap = new WrapContainer { HorizontalExpand = true, SeparationOverride = 4, CrossSeparationOverride = 4 };
        var labels = row.Buttons ?? Array.Empty<string>();
        for (var i = 0; i < labels.Length; i++)
        {
            var button = InertButton(labels[i], SWConsoleStyle.Accent);
            // As in the game, the option already in effect cannot be chosen again; a disabled row turns every button off.
            // A row of toggles (Chosen) keeps the current one available and shows it pressed instead.
            button.Disabled = row.Disabled || !row.Chosen && i == row.Current;
            if (row.Chosen)
                SWConsoleStyle.StyleTabButton(button, i == row.Current);
            wrap.AddChild(Slot(button, mark && i == row.TargetIndex));
            if (i == row.TargetIndex)
                target = button;
        }

        return wrap;
    }

    /// <summary>Confirmed labels only: no amounts and no list of ministries, which depend on the round.</summary>
    private static void BuildBudgetStatus(BoxContainer box, SWTrainingBudgetStatusPanel state)
    {
        var appRow = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 10 };
        appRow.AddChild(SWConsoleStyle.Text("Финансы", SWConsoleStyle.Brass, SWConsoleStyle.Strong));
        appRow.AddChild(SWConsoleStyle.Text("Бюджет", SWConsoleStyle.TextBright, SWConsoleStyle.Strong));
        box.AddChild(appRow);

        var phaseText = state.Phase == SWTrainingBudgetPhase.Approved ? "[одобрен]" : state.Phase == SWTrainingBudgetPhase.Pending ? "[на одобрении]" : "[проект не подан]";
        box.AddChild(SWConsoleStyle.Wrap(phaseText, SWConsoleStyle.TextBright));
        if (state.AutoReason != null)
            box.AddChild(SWConsoleStyle.Wrap($"Стандартное распределение: {state.AutoReason}", SWConsoleStyle.TextBright));
        if (state.Phase == SWTrainingBudgetPhase.NotSubmitted)
            box.AddChild(SWConsoleStyle.Wrap("Минфин ещё не подал проект бюджета на сегодня.", SWConsoleStyle.TextDim));
    }

    private static PanelContainer AmountRow(string name, int amount)
    {
        var panel = SWConsoleStyle.Row(SWConsoleStyle.BgPanel);
        var line = new BoxContainer { Orientation = LayoutOrientation.Horizontal, HorizontalExpand = true, SeparationOverride = 6 };
        var text = SWConsoleStyle.Wrap(name, SWConsoleStyle.TextBright);
        line.AddChild(text);
        line.AddChild(SWConsoleStyle.Text($"{amount} кр.", SWConsoleStyle.TextBright, SWConsoleStyle.Body));
        panel.AddChild(line);
        return panel;
    }

    private static BoxContainer SectorRow(string name, int amount)
    {
        var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 6 };
        var label = SWConsoleStyle.Text(name, SWConsoleStyle.TextBright, SWConsoleStyle.Strong);
        label.HorizontalExpand = true;
        row.AddChild(label);
        row.AddChild(InertField(amount.ToString()));
        return row;
    }

    private static int Pool(int percent) => ExampleDayBudget * percent / 100;

    private static PanelContainer Segment(Color color, int percent) => new()
    {
        HorizontalExpand = true,
        MinHeight = 10,
        SizeFlagsStretchRatio = Math.Max(1, percent),
        PanelOverride = new StyleBoxFlat { BackgroundColor = color },
    };
}
