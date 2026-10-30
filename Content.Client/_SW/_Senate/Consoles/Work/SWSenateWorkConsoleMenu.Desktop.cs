using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Robust.Client.Graphics;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Empire;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client._SW._Senate.Consoles.Work;

public sealed partial class SWSenateWorkConsoleMenu
{
    private sealed record DesktopApp(string Name, (Button? Nav, Control Panel, string Name)[] Sections);
    private readonly List<DesktopApp> _desktopApps = new();
    private readonly List<DesktopApp> _recentApps = new();
    private readonly Dictionary<DesktopApp, Control> _lastSections = new();
    private DesktopApp? _activeApp;
    private bool _desktopInitialized;
    private string _desktopAppsSignature = string.Empty;
    private string _desktopShellSignature = string.Empty;
    private string _desktopTasksSignature = string.Empty;

    private void InitializeDesktop()
    {
        _desktopApps.Add(new("Законы", new (Button?, Control, string)[] { (NavAgenda, AgendaPanel, "Документы"), (NavDraft, DraftTab, "Редактор") }));
        _desktopApps.Add(new("Мой сектор", new (Button?, Control, string)[] { (NavMap, MapTab, "Карта"), (NavSummary, DashScroll, "Сводка"), (NavDilemmas, DilemmaTab, "Дела сектора"), (NavCrises, CrisesTab, "Кризисы") }));
        _desktopApps.Add(new("Заседания", new (Button?, Control, string)[] { (NavSchedule, ScheduleTab, "Расписание") }));
        _desktopApps.Add(new("Комитеты", new (Button?, Control, string)[] { (NavCommittees, CommitteesTab, "Комитеты") }));
        _desktopApps.Add(new("Политика", new (Button?, Control, string)[] { (NavParties, PartiesTab, "Партии"), (NavAmbitions, AmbitionsTab, "Амбиции") }));
        _desktopApps.Add(new("Состав Сената", new (Button?, Control, string)[] { (NavRoster, RosterScroll, "Состав") }));
        _desktopApps.Add(new("Финансы", new (Button?, Control, string)[] { (NavBudget, BudgetTab, "Бюджет") }));
        _desktopApps.Add(new("Дипломатия", new (Button?, Control, string)[] { (NavDiplomacy, DiplomacyTab, "Дипломатия") }));
        _desktopApps.Add(new("Канцелярия", new (Button?, Control, string)[] { (NavSignature, SignaturePanel, "На подпись"), (NavDecree, DecreeTab, "Декрет"), (NavCourt, CourtTab, "Суд"), (NavAnnounce, AnnounceTab, "Объявления"), (null, ApprovalTab, "Согласования") }));
        _desktopApps.Add(new("Профиль", new (Button?, Control, string)[] { (NavOffice, OfficeTab, "Должность"), (NavPowers, PowersScroll, "Полномочия") }));
        HomeButton.OnPressed += _ => ShowDesktop();
        HeaderProfile.OnPressed += _ => ShowSection(OfficeTab);
        HeaderProfile.SetAppearance(SWConsoleStyle.Brass, quiet: true);
        _desktopInitialized = true;
    }

    private bool ApprovalAvailable(SWSenateWorkConsoleState s) => s.CanApproveWar || s.CanApproveEmergency || s.CanApproveHealthEmergency || s.CanApproveStimulus || s.CanApproveEmission || s.CanApproveTribunal || s.CanApproveSecuritySabotage || (s.IsSenateCard && (s.WarProposed || s.EmergencyProposed || s.StimulusProposed || s.EmissionProposed || s.TribunalProposed || s.HealthQuarantines.Count > 0 || s.SecuritySabotageRequests.Count > 0));
    private bool Available((Button? Nav, Control Panel, string Name) section) => section.Nav?.Visible ?? (_lastState is { } s && ApprovalAvailable(s));
    private bool Available(DesktopApp app) => app.Sections.Any(Available);
    private string AppName(DesktopApp app) => app.Name == "Мой сектор" && _lastState is { Position: not SWSenatePosition.Senator } ? "Республика" : app.Name;

    private SWConsoleButton DesktopButton(string text, Action action)
    {
        var button = new SWConsoleButton { Text = text, HorizontalExpand = true, MinHeight = 36, Margin = new Thickness(0, 0, 0, 3) };
        button.OnPressed += _ => action();
        return button;
    }

    private void ShowDesktop()
    {
        if (!_desktopInitialized) return;
        _scheduleBoard.CancelInteraction();
        _activeApp = null;
        foreach (var (_, panel) in _sections) panel.Visible = false;
        ApprovalTab.Visible = false;
        ContentHost.Visible = false;
        ShellBar.Visible = false;
        DesktopScroll.Visible = true;
        RefreshDesktop();
    }

    private DesktopTextButton DesktopTextCard(string text, Action action, string? time = null)
    {
        var button = new DesktopTextButton { HorizontalExpand = true, Margin = new Thickness(0, 0, 0, 3) };
        var content = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            MouseFilter = MouseFilterMode.Ignore,
        };
        if (time != null)
            content.AddChild(new Label { Text = time, FontOverride = SWConsoleStyle.Small, FontColorOverride = SWConsoleStyle.Brass, MouseFilter = MouseFilterMode.Ignore });
        var description = SWConsoleStyle.Wrap(text, SWConsoleStyle.TextBright);
        description.MouseFilter = MouseFilterMode.Ignore;
        content.AddChild(description);
        button.AddChild(content);
        button.OnPressed += _ => action();
        return button;
    }

    private bool PrepareDesktopSection(Control target)
    {
        if (!_desktopInitialized) return true;
        var app = _desktopApps.FirstOrDefault(a => a.Sections.Any(s => s.Panel == target));
        if (app == null || !Available(app) || !app.Sections.Any(s => s.Panel == target && Available(s))) return false;
        _activeApp = app;
        _lastSections[app] = target;
        DesktopScroll.Visible = false;
        ContentHost.Visible = true;
        ShellBar.Visible = true;
        if (app.Name != "Профиль")
        {
            _recentApps.Remove(app);
            _recentApps.Insert(0, app);
            if (_recentApps.Count > 4) _recentApps.RemoveAt(4);
        }
        RefreshDesktop();
        return true;
    }

    private void OpenApp(DesktopApp app)
    {
        var section = _lastSections.TryGetValue(app, out var previous)
            ? app.Sections.FirstOrDefault(s => s.Panel == previous && Available(s))
            : default;
        if (section.Panel == null) section = app.Sections.FirstOrDefault(Available);
        if (section.Panel != null) ShowSection(section.Panel);
    }

    private void RefreshDesktop()
    {
        if (!_desktopInitialized || _lastState is not { } state) return;
        HeaderProfile.Text = state.IsSenateCard ? state.PositionLabel : "Профиль";
        var apps = _desktopApps.Where(a => a.Name != "Профиль" && Available(a)).ToArray();
        var appsSignature = string.Join("|", apps.Select(AppName));
        if (_desktopAppsSignature != appsSignature)
        {
            _desktopAppsSignature = appsSignature;
            DesktopApps.RemoveAllChildren();
            foreach (var app in apps)
            {
                var tile = new DesktopTile { HorizontalExpand = true, MinHeight = 110 };
                var content = new BoxContainer
                {
                    Orientation = BoxContainer.LayoutOrientation.Vertical,
                    HorizontalExpand = true,
                    MouseFilter = MouseFilterMode.Ignore,
                    SeparationOverride = 6,
                };
                content.AddChild(new DesktopIcon(_desktopApps.IndexOf(app)));
                var caption = new Label
                {
                    Text = AppName(app),
                    FontOverride = SWConsoleStyle.Strong,
                    FontColorOverride = SWConsoleStyle.TextBright,
                    HorizontalAlignment = HAlignment.Center,
                    MouseFilter = MouseFilterMode.Ignore,
                };
                content.AddChild(caption);
                tile.AddChild(content);
                tile.OnPressed += _ => OpenApp(app);
                DesktopApps.AddChild(tile);
            }
        }
        _recentApps.RemoveAll(a => !Available(a));
        var shellSignature = (_activeApp == null ? "" : AppName(_activeApp)) + ":" +
            (_activeApp == null ? "" : string.Join("|", _activeApp.Sections.Where(Available).Select(s => s.Name))) + ":" +
            string.Join("|", _recentApps.Select(AppName));
        if (_desktopShellSignature != shellSignature)
        {
            _desktopShellSignature = shellSignature;
            LocalNav.RemoveAllChildren();
            if (_activeApp != null)
            {
                AppTitle.Text = AppName(_activeApp);
                foreach (var section in _activeApp.Sections.Where(Available))
                    LocalNav.AddChild(DesktopButton(section.Name, () => ShowSection(section.Panel)));
            }
            RecentBar.RemoveAllChildren();
            foreach (var app in _recentApps)
                RecentBar.AddChild(DesktopButton(AppName(app), () => OpenApp(app)));
            RecentBar.Visible = _recentApps.Count > 0 && _activeApp?.Sections.Any(s => s.Panel == ScheduleTab) != true;
            LocalNav.Visible = _activeApp?.Sections.Any(s => s.Panel == ScheduleTab) != true;
        }
        RefreshDesktopTasks(state);
    }

    private void UpdateDesktop(SWSenateWorkConsoleState state, SWSenateWorkConsoleState? previous)
    {
        if (!_desktopInitialized) return;
        var contextChanged = previous != null && (previous.CardIdentity != state.CardIdentity || previous.HasCard != state.HasCard || previous.IsSenateCard != state.IsSenateCard || previous.Position != state.Position || previous.SectorId != state.SectorId || previous.InOffice != state.InOffice);
        if (contextChanged)
        {
            _recentApps.Clear(); _lastSections.Clear();
            _expandedDilemmas.Clear(); _expandedCrises.Clear(); _expandedParties.Clear();
            _selectedImperialAgreement = null;
            _budgetDraft.Clear(); _budgetDraftKey = string.Empty;
            CancelRevision();
            DecreeNameEdit.Text = string.Empty; DecreeDescEdit.Text = string.Empty;
            PartyNameEdit.Text = string.Empty; PartyDescEdit.Text = string.Empty;
            CommitteeReasonEdit.Text = string.Empty; AnnounceEdit.Text = string.Empty; DiploReplyEdit.Text = string.Empty;
            DraftSearch.Text = string.Empty; _draftSuggestions.Clear(); _suggestionSnapshot = string.Empty;
            DraftSuggestPanel.Visible = false;
            var oldMap = SectorMap;
            MapTab.RemoveAllChildren(); SectorMap = null;
            oldMap?.Dispose();
            UpdateMap(state);
            foreach (var slider in _decreeSliders.Values) slider.Value = 0;
            _selectedBillId = -1; _selectedStartHour = -1; _groupPrimaryBillId = -1;
            _groupedBillIds.Clear();
            ResetScheduleContext();
            _agendaDocuments.ResetLocalState();
            _signatureDocuments.ResetLocalState();
            SectorPrograms.ResetLocalState();
            ShowDesktop();
        }
        if ((previous == null || contextChanged) && (!state.HasCard || !state.IsSenateCard || !state.InOffice))
            ShowSection(OfficeTab);
        else if (_activeApp != null && (!Available(_activeApp) || !_activeApp.Sections.Any(s => s.Panel.Visible && Available(s))))
            ShowDesktop();
        RefreshDesktop();
    }


    private sealed class DesktopIcon : Control
    {
        private readonly int _kind;
        public DesktopIcon(int kind)
        {
            _kind = kind;
            SetSize = new Vector2(48, 48);
            HorizontalAlignment = HAlignment.Center;
            MouseFilter = MouseFilterMode.Ignore;
            VerticalAlignment = VAlignment.Center;
        }

        protected override void Draw(DrawingHandleScreen handle)
        {
            base.Draw(handle);
            var ink = Color.ToSrgb(SWConsoleStyle.Brass);
            var bg = Color.ToSrgb(SWConsoleStyle.Navigation);
            var c = PixelSize / 2;
            var r = MathF.Min(PixelWidth, PixelHeight) * 0.35f;
            switch (_kind)
            {
                case 0: // Document
                    Rect(handle, c.X-r*.65f, c.Y-r, r*1.3f, r*2, ink);
                    for (var i = 0; i < 3; i++)
                        Rect(handle, c.X-r*.45f, c.Y-r*.55f+i*r*.45f, r*.9f, r*.13f, bg);
                    break;
                case 1: // Globe
                    handle.DrawCircle(c, r, ink);
                    handle.DrawCircle(c, r*.78f, bg);
                    Rect(handle, c.X-r*.08f, c.Y-r*.78f, r*.16f, r*1.56f, ink);
                    Rect(handle, c.X-r*.78f, c.Y-r*.08f, r*1.56f, r*.16f, ink);
                    break;
                case 2: // Calendar
                    Rect(handle, c.X-r*.9f, c.Y-r*.75f, r*1.8f, r*1.6f, ink);
                    Rect(handle, c.X-r*.7f, c.Y-r*.35f, r*1.4f, r, bg);
                    Rect(handle, c.X-r*.5f, c.Y-r, r*.2f, r*.45f, ink);
                    Rect(handle, c.X+r*.3f, c.Y-r, r*.2f, r*.45f, ink);
                    Rect(handle, c.X-r*.5f, c.Y, r*.35f, r*.3f, ink);
                    Rect(handle, c.X+r*.15f, c.Y, r*.35f, r*.3f, ink);
                    break;
                case 3: // Committee
                    handle.DrawCircle(c + new Vector2(-r*.5f, -r*.35f), r*.3f, ink);
                    handle.DrawCircle(c + new Vector2(r*.5f, -r*.35f), r*.3f, ink);
                    handle.DrawCircle(c + new Vector2(0, -r*.55f), r*.35f, ink);
                    Rect(handle, c.X-r*.9f, c.Y+r*.1f, r*1.8f, r*.55f, ink);
                    break;
                case 4: // Political institution
                    Rect(handle, c.X-r, c.Y-r*.8f, r*2, r*.25f, ink);
                    for (var i = 0; i < 3; i++)
                        Rect(handle, c.X-r*.75f+i*r*.65f, c.Y-r*.45f, r*.2f, r, ink);
                    Rect(handle, c.X-r, c.Y+r*.65f, r*2, r*.25f, ink);
                    break;
                case 5: // Roster
                    for (var i = 0; i < 3; i++)
                    {
                        handle.DrawCircle(c + new Vector2(-r*.65f, -r*.6f+i*r*.6f), r*.19f, ink);
                        Rect(handle, c.X-r*.25f, c.Y-r*.7f+i*r*.6f, r*1.1f, r*.2f, ink);
                    }
                    break;
                case 6: // Finance
                    handle.DrawCircle(c, r, ink);
                    handle.DrawCircle(c, r*.77f, bg);
                    Rect(handle, c.X-r*.1f, c.Y-r*.65f, r*.2f, r*1.3f, ink);
                    Rect(handle, c.X-r*.5f, c.Y-r*.28f, r, r*.18f, ink);
                    Rect(handle, c.X-r*.5f, c.Y+r*.15f, r, r*.18f, ink);
                    break;
                case 7: // Diplomacy
                    handle.DrawCircle(c + new Vector2(-r*.48f, -r*.5f), r*.3f, ink);
                    handle.DrawCircle(c + new Vector2(r*.48f, -r*.5f), r*.3f, ink);
                    Rect(handle, c.X-r*.8f, c.Y-r*.05f, r*.45f, r*.9f, ink);
                    Rect(handle, c.X+r*.35f, c.Y-r*.05f, r*.45f, r*.9f, ink);
                    Rect(handle, c.X-r*.4f, c.Y+r*.15f, r*.8f, r*.25f, ink);
                    break;
                default: // Office
                    Rect(handle, c.X-r*.9f, c.Y-r*.65f, r*1.8f, r*1.4f, ink);
                    Rect(handle, c.X-r*.65f, c.Y-r*.4f, r*1.3f, r*.85f, bg);
                    Rect(handle, c.X-r*.38f, c.Y-r, r*.76f, r*.3f, ink);
                    Rect(handle, c.X-r*.2f, c.Y-r*.05f, r*.4f, r*.2f, ink);
                    break;
            }
        }

        private static void Rect(DrawingHandleScreen handle, float x, float y, float width, float height, Color color)
        {
            var points = new[] { new Vector2(x, y), new Vector2(x + width, y), new Vector2(x + width, y + height), new Vector2(x, y + height) };
            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, points, color);
        }
    }

    private void RefreshDesktopTasks(SWSenateWorkConsoleState state)
    {
        var tasks = new List<(string Text, Action Open)>();
        if (state.CanTake || state.IsCourtNominee || state.IsChancellorElect) tasks.Add(("Должность и назначение", () => ShowSection(OfficeTab)));
        if (state.CanReviseBills && state.ReturnedBills.Count > 0)
        {
            var id = state.ReturnedBills[0].Id;
            tasks.Add(($"На доработку: {state.ReturnedBills.Count}", () => { if (_lastState is { CanReviseBills: true } fresh && fresh.ReturnedBills.FirstOrDefault(b => b.Id == id) is { } bill) LoadRevision(bill); }));
        }
        if (state.CanSignLaws && state.LawsAwaitingSignature.Count > 0)
        {
            var id = state.LawsAwaitingSignature[0].Id;
            tasks.Add(($"На подпись: {state.LawsAwaitingSignature.Count}", () => { if (_lastState is { CanSignLaws: true } fresh && fresh.LawsAwaitingSignature.Any(l => l.Id == id)) { ShowSection(SignaturePanel); _signatureDocuments.SelectDocument(id, true); } }));
        }
        var dilemmas = state.MyDilemmas.Count(d => d.CanResolve) + state.MyBudgetDilemmas.Count(d => d.CanResolve);
        if (state.CanDilemmas && dilemmas > 0) tasks.Add(($"Дела сектора: {dilemmas}", () => ShowSection(DilemmaTab)));
        if (state.CanConfirmSessions && state.PendingSessions.Count > 0) tasks.Add(($"Заседания на подтверждение: {state.PendingSessions.Count}", () => ShowSection(ScheduleTab)));
        if (state.BudgetProjectPending && state.CanApproveBudget && NavBudget.Visible) tasks.Add(("Проект бюджета", () => ShowSection(BudgetTab)));
        var approvals = (state.WarProposed && state.CanApproveWar ? 1 : 0) + (state.EmergencyProposed && state.CanApproveEmergency ? 1 : 0) + (state.StimulusProposed && state.CanApproveStimulus ? 1 : 0) + (state.EmissionProposed && state.CanApproveEmission ? 1 : 0) + (state.TribunalProposed && state.CanApproveTribunal ? 1 : 0) + (state.CanApproveSecuritySabotage ? state.SecuritySabotageRequests.Count : 0) + (state.CanApproveHealthEmergency ? state.HealthQuarantines.Count(q => q.Pending) : 0);
        if (approvals > 0) tasks.Add(($"Согласования: {approvals}", () => ShowSection(ApprovalTab)));
        var diplomacy = state.CanDecideDiplomacy ? state.ImperialDiplomacy.Agreements.Count(a => a.Status == SWEmpireDiplomacyStatus.AwaitingApproval) : 0;
        if (diplomacy > 0)
        {
            var id = state.ImperialDiplomacy.Agreements.First(a => a.Status == SWEmpireDiplomacyStatus.AwaitingApproval).Id;
            tasks.Add(($"Договоры на согласование: {diplomacy}", () =>
            {
                if (_lastState is not { CanDecideDiplomacy: true } fresh ||
                    !fresh.ImperialDiplomacy.Agreements.Any(a => a.Id == id && a.Status == SWEmpireDiplomacyStatus.AwaitingApproval))
                    return;
                _selectedImperialAgreement = id;
                _diplomacySignature = string.Empty;
                ShowSection(DiplomacyTab);
            }));
        }
        var row = state.Schedule.FirstOrDefault(r => r.IsNow);
        var taskSignature = string.Join("|", tasks.Select(t => t.Text)) + ":" +
            state.ReturnedBills.FirstOrDefault()?.Id + ":" + state.LawsAwaitingSignature.FirstOrDefault()?.Id + ":" +
            state.ImperialDiplomacy.Agreements.FirstOrDefault(a => a.Status == SWEmpireDiplomacyStatus.AwaitingApproval)?.Id + ":" +
            state.CardIdentity + ":" + row?.TimeLabel + ":" + row?.Text + ":" + NavSchedule.Visible;
        if (_desktopTasksSignature == taskSignature) return;
        _desktopTasksSignature = taskSignature;
        DesktopTasks.RemoveAllChildren();
        foreach (var task in tasks) DesktopTasks.AddChild(DesktopTextCard(task.Text, task.Open));
        if (tasks.Count == 0) DesktopTasks.AddChild(SWConsoleStyle.Text("Нет ожидающих решений", SWConsoleStyle.TextDim, SWConsoleStyle.Body));
        DesktopSchedule.RemoveAllChildren();
        if (row != null) DesktopSchedule.AddChild(DesktopTextCard(row.Text, () => ShowSection(ScheduleTab), row.TimeLabel));
        else if (NavSchedule.Visible) DesktopSchedule.AddChild(DesktopButton("Открыть расписание дня", () => ShowSection(ScheduleTab)));
    }
}

/// <summary>Measures both desktop columns at their actual width and unconstrained height.</summary>
public sealed class SenateDesktopColumns : Container
{
    private const float Breakpoint = 900;
    private const float Gap = 24;
    private bool _wide;

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        if (ChildCount < 2)
            return Vector2.Zero;

        var first = Children[0];
        var second = Children[1];
        var width = float.IsFinite(availableSize.X) ? availableSize.X : Breakpoint;
        var wide = width >= Breakpoint;
        _wide = wide;
        var firstWidth = wide ? (width - Gap) * 0.60f : width;
        var secondWidth = wide ? width - firstWidth - Gap : width;
        if (first is BoxContainer box && box.Children.OfType<GridContainer>().FirstOrDefault() is { } grid)
        {
            var columns = wide ? 3 : 2;
            if (grid.Columns != columns)
                grid.Columns = columns;
        }

        first.Measure(new Vector2(firstWidth, float.PositiveInfinity));
        second.Measure(new Vector2(secondWidth, float.PositiveInfinity));
        var height = wide ? MathF.Max(first.DesiredSize.Y, second.DesiredSize.Y)
            : first.DesiredSize.Y + Gap + second.DesiredSize.Y;
        return new Vector2(width, height);
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        if (ChildCount < 2)
            return finalSize;
        var first = Children[0];
        var second = Children[1];
        if (_wide)
        {
            var firstWidth = (finalSize.X - Gap) * 0.60f;
            first.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(firstWidth, first.DesiredSize.Y)));
            second.Arrange(UIBox2.FromDimensions(new Vector2(firstWidth + Gap, 0),
                new Vector2(finalSize.X - firstWidth - Gap, second.DesiredSize.Y)));
        }
        else
        {
            first.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(finalSize.X, first.DesiredSize.Y)));
            second.Arrange(UIBox2.FromDimensions(new Vector2(0, first.DesiredSize.Y + Gap),
                new Vector2(finalSize.X, second.DesiredSize.Y)));
        }
        return finalSize;
    }
}

/// <summary>A quiet, fully clickable desktop shortcut.</summary>
public sealed class DesktopTile : ContainerButton
{
    protected override void DrawModeChanged()
    {
        base.DrawModeChanged();
        StyleBoxOverride = new StyleBoxFlat
        {
            BackgroundColor = DrawMode is DrawModeEnum.Hover or DrawModeEnum.Pressed
                ? SWConsoleStyle.Hover : Color.Transparent,
            ContentMarginLeftOverride = 4, ContentMarginRightOverride = 4,
            ContentMarginTopOverride = 10, ContentMarginBottomOverride = 10,
        };
    }
}

/// <summary>A wrapping, fully clickable desktop item.</summary>
public sealed class DesktopTextButton : ContainerButton
{
    protected override void DrawModeChanged()
    {
        base.DrawModeChanged();
        StyleBoxOverride = new StyleBoxFlat
        {
            BackgroundColor = DrawMode is DrawModeEnum.Hover or DrawModeEnum.Pressed
                ? SWConsoleStyle.Hover : SWConsoleStyle.BgRowAlt,
            ContentMarginLeftOverride = 10, ContentMarginRightOverride = 10,
            ContentMarginTopOverride = 6, ContentMarginBottomOverride = 6,
        };
    }
}
