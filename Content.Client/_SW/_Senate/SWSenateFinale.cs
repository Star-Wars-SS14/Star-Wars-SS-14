using System;
using System.Numerics;
using Content.Shared._SW._Senate;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using static Robust.Client.UserInterface.Controls.BoxContainer;

namespace Content.Client._SW._Senate;

/// <summary>Round-end credits. Only the controls intercept input; the backdrop does not.</summary>
public sealed class SWSenateFinale : LayoutContainer
{
    private readonly ScrollContainer _scroll;
    private readonly BoxContainer _content;
    private readonly Control _lead;
    private readonly Control _tail;
    private readonly Button _pause;
    private readonly Action _finish;
    private float _elapsed;
    private bool _paused;
    private bool _finished;
    private static Font? _titleFont;
    private static Font? _nameFont;
    private static Font? _headingFont;
    private static Font? _factFont;

    private static Font LoadFont(string variant, int size) =>
        new VectorFont(IoCManager.Resolve<IResourceCache>().GetResource<FontResource>($"/Fonts/NotoSans/NotoSans-{variant}.ttf"), size);

    private static RichTextLabel CreditText(string text, Font font, Color color, float top = 0, float bottom = 0)
    {
        var label = new SWConsoleStyle.WrappingRichTextLabel(720)
        {
            HorizontalAlignment = HAlignment.Center,
            MouseFilter = MouseFilterMode.Ignore,
            Margin = new Thickness(0, top, 0, bottom),
            Stylesheet = new Stylesheet(new StyleRule[] { StylesheetHelpers.Element<RichTextLabel>().Prop("font", font) }),
        };
        var message = new FormattedMessage();
        message.PushColor(color);
        message.AddText(text);
        message.Pop();
        label.SetMessage(message);
        return label;
    }

    public SWSenateFinale(SWSenateRoundHistory history, TimeSpan duration, Action finish)
    {
        _finish = finish;
        MouseFilter = MouseFilterMode.Ignore;
        _scroll = new ScrollContainer { HScrollEnabled = false, MouseFilter = MouseFilterMode.Ignore, MaxWidth = 760 };
        AddChild(_scroll);
        SetAnchorLeft(_scroll, 0f);
        SetAnchorRight(_scroll, 1f);
        SetAnchorTop(_scroll, 0f);
        SetAnchorBottom(_scroll, 1f);
        SetMarginLeft(_scroll, 24);
        SetMarginRight(_scroll, -24);
        SetMarginTop(_scroll, 24);
        SetMarginBottom(_scroll, -88);
        _content = new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, MouseFilter = MouseFilterMode.Ignore };
        _scroll.AddChild(_content);
        _lead = new Control { MouseFilter = MouseFilterMode.Ignore };
        _content.AddChild(_lead);
        var heading = _headingFont ??= LoadFont("Bold", 15);
        var fact = _factFont ??= LoadFont("Regular", 15);
        var nameFont = _nameFont ??= LoadFont("Regular", 20);
        _content.AddChild(CreditText("ПОСЛЕДНЕЕ ЗАСЕДАНИЕ", heading, SWConsoleStyle.Brass, bottom: 12));
        _content.AddChild(CreditText("Созыв завершён", _titleFont ??= LoadFont("Bold", 24), SWConsoleStyle.TextBright, bottom: 24));
        _content.AddChild(CreditText($"Игровых дней: {history.Days}", fact, SWConsoleStyle.TextDim, bottom: 8));
        _content.AddChild(CreditText($"Длительность: {(int) duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}", fact, SWConsoleStyle.TextDim, bottom: 8));
        _content.AddChild(CreditText($"Принято законов: {history.PassedLaws}, поправок: {history.PassedAmendments}", fact, SWConsoleStyle.TextDim));
        _content.AddChild(CreditText("УЧАСТНИКИ СОЗЫВА", heading, SWConsoleStyle.Brass, top: 64, bottom: 28));
        if (history.Participants.Length == 0)
            _content.AddChild(CreditText("Участники не зафиксированы.", fact, SWConsoleStyle.TextDim, bottom: 28));
        foreach (var name in history.Participants)
            _content.AddChild(CreditText(name, nameFont, SWConsoleStyle.TextBright, bottom: 28));
        void Section(string title) => _content.AddChild(CreditText(title, heading, SWConsoleStyle.Brass, top: 64, bottom: 28));
        void Line(string text) => _content.AddChild(CreditText(text, fact, SWConsoleStyle.TextBright, bottom: 20));
        Section("РЕСПУБЛИКА");
        if (history.Stats.Length == 0) Line("Показатели недоступны.");
        foreach (var stat in history.Stats)
            Line($"{stat.Name}: начало {stat.Start:0.0}; итог {stat.End:0.0}; изменение {stat.End - stat.Start:+0.0;-0.0;0.0}");
        Section("СЕКТОРА");
        if (history.Sectors.Length == 0) Line("Данные о секторах недоступны.");
        foreach (var sector in history.Sectors)
        {
            Line(sector.Name);
            foreach (var stat in sector.Stats)
                Line($"{stat.Name}: начало {stat.Start:0.0}; итог {stat.End:0.0}; изменение {stat.End - stat.Start:+0.0;-0.0;0.0}");
        }
        Section("ЗАКОНЫ И ПОПРАВКИ");
        if (history.Laws.Length == 0) Line("Принятых законов не зафиксировано.");
        foreach (var law in history.Laws)
            Line($"{law.Name} — автор: {law.Author}{(law.Amendment ? " (поправка)" : "")}{(law.Repealed ? " (отменён)" : "")}");
        if (history.AdditionalLaws > 0) Line($"Других решений: {history.AdditionalLaws}.");
        Section("ВЕДОМСТВА И СУД");
        foreach (var line in history.DepartmentSummary) Line(line);
        Line("Подтверждённая история остальных ведомств недоступна.");
        Section("ЗАВЕРШЁННЫЕ ПРОЕКТЫ");
        if (history.Projects.Length == 0) Line("Завершённые проекты не зафиксированы.");
        foreach (var project in history.Projects) Line(project);
        Section("ЭКОНОМИЧЕСКИЕ СОБЫТИЯ · ПОСЛЕДНИЕ 20");
        if (history.Events.Length == 0) Line("Экономические события не зафиксированы.");
        foreach (var ev in history.Events) Line(ev);
        if (history.CloseVotes.Length > 0)
        {
            Section("ЗАЛ РАЗДЕЛИЛСЯ");
            foreach (var vote in history.CloseVotes)
                Line($"{vote.Name} — голоса секторов: за {vote.For}, против {vote.Against}");
        }
        _tail = new Control { MouseFilter = MouseFilterMode.Ignore };
        _content.AddChild(_tail);

        var actions = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 10, MouseFilter = MouseFilterMode.Ignore };
        AddChild(actions);
        SetAnchorPreset(actions, LayoutPreset.BottomWide);
        SetMarginLeft(actions, 24);
        SetMarginRight(actions, -24);
        SetMarginTop(actions, -60);
        SetMarginBottom(actions, -16);
        _pause = new Button { Text = "Пауза" };
        _pause.OnPressed += _ => { _paused = !_paused; _pause.Text = _paused ? "Продолжить" : "Пауза"; };
        actions.AddChild(_pause);
        var replay = new Button { Text = "С начала" };
        replay.OnPressed += _ => Replay();
        actions.AddChild(replay);
        actions.AddChild(new Control { HorizontalExpand = true, MouseFilter = MouseFilterMode.Ignore });
        var skip = new Button { Text = "К итогам" };
        skip.OnPressed += _ => Complete();
        actions.AddChild(skip);
    }

    private void Replay()
    {
        _elapsed = 0;
        _scroll.VScroll = 0;
        _paused = false;
        _pause.Text = "Пауза";
    }

    private void Complete()
    {
        if (_finished) return;
        _finished = true;
        _finish();
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var height = float.IsFinite(availableSize.Y) ? availableSize.Y : 720f;
        // The lead leaves the heading visible on first layout, including an empty session.
        var lead = Math.Max(1, height - 240f);
        var tail = Math.Max(1, height - 112f);
        if (_lead.MinHeight != lead) _lead.MinHeight = lead;
        if (_tail.MinHeight != tail) _tail.MinHeight = tail;
        return base.MeasureOverride(availableSize);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (_paused || _finished) return;
        _elapsed += args.DeltaSeconds;
        if (_elapsed < 1f || _scroll.Height <= 0 || _content.Height <= _scroll.Height) return;
        _scroll.VScroll += args.DeltaSeconds * 32f;
        if (_scroll.VScroll >= _content.Height - _scroll.Height - 1f)
            Complete();
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var alpha = Math.Clamp(_elapsed / 0.8f, 0f, 1f) * 0.9f;
        handle.DrawRect(new UIBox2(0, 0, PixelWidth, PixelHeight), Color.FromHex("#0D1118").WithAlpha(alpha));
    }
}
