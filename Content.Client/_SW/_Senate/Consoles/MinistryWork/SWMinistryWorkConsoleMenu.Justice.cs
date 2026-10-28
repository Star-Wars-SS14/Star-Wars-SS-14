using System;
using System.Collections.Generic;
using System.Linq;
using Content.Client.Message;
using Content.Client._SW._Senate.Consoles.Statistics;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Ministries;
using Content.Shared._SW._Senate.Sectors;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;

namespace Content.Client._SW._Senate.Consoles.MinistryWork;

public sealed partial class SWMinistryWorkConsoleMenu
{
    private SWJusticeMapPanel? _justiceMap;
    private SWJusticeDossiers? _justiceDossiers, _justiceArchive;
    private SWJusticeJournal? _justiceJournal;

    private void BuildJusticeInspections(SWMinistryWorkConsoleState state)
    {
        if (!state.IsJustice) return;
        if (_justiceMap == null)
        {
            _justiceMap = new SWJusticeMapPanel();
            _justiceMap.OpenRequested += _owner.OpenJusticeInspection;
            _justiceMap.CaseSelected += id => { _justiceDossiers?.SelectCase(id); ShowTab(CasesTab); };
            JusticePanel.AddChild(_justiceMap);
            _justiceDossiers = new SWJusticeDossiers(false);
            _justiceDossiers.CheckRequested += _owner.RunJusticeCheck;
            _justiceDossiers.ConcludeRequested += _owner.ConcludeJusticeInspection;
            _justiceDossiers.RemedyRequested += _owner.ApplyJusticeRemedy;
            _justiceDossiers.FollowUpRequested += _owner.FollowUpJusticeInspection;
            CasesPanel.AddChild(_justiceDossiers);
            _justiceArchive = new SWJusticeDossiers(true);
            ArchivePanel.AddChild(_justiceArchive);
            _justiceJournal = new SWJusticeJournal();
            _justiceJournal.CaseSelected += (id, archived) =>
            {
                if (archived) { _justiceArchive?.SelectCase(id); ShowTab(ArchiveTab); }
                else { _justiceDossiers?.SelectCase(id); ShowTab(CasesTab); }
            };
            JusticeJournalPanel.AddChild(_justiceJournal);
        }
        _justiceMap.SetState(state.JusticeInspections);
        _justiceDossiers!.SetState(state.JusticeInspections);
        _justiceArchive!.SetState(state.JusticeInspections);
        _justiceJournal!.SetState(state.JusticeInspections);
    }
}

public sealed class SWJusticeMapPanel : BoxContainer
{
    private readonly SWGalacticMapPrototype _source = IoCManager.Resolve<IPrototypeManager>().Index<SWGalacticMapPrototype>("SenateRepublic");
    private readonly BoxContainer _host = new() { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, VerticalExpand = true };
    private readonly BoxContainer _details = new() { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, SeparationOverride = 6 };
    private readonly ScrollContainer _scroll = new() { HScrollEnabled = false, HorizontalExpand = true, VerticalExpand = true };
    private readonly Label _title = SWConsoleStyle.Text("Галактика", SWConsoleStyle.Brass, SWConsoleStyle.Strong);
    private readonly Label _zoom = SWConsoleStyle.Text("100%", SWConsoleStyle.TextDim, SWConsoleStyle.Small);
    private readonly Button _galaxy = SWConsoleStyle.TabButton("Галактика");
    private SWGalacticMapControl _map = default!;
    private SWJusticeInspectionState? _state;
    private string? _sector, _world, _revision;
    public event Action<string>? OpenRequested;
    public event Action<int>? CaseSelected;

    public SWJusticeMapPanel()
    {
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = VerticalExpand = true;
        var bar = new WrapContainer { HorizontalExpand = true, SeparationOverride = 5, CrossSeparationOverride = 4 };
        _galaxy.OnPressed += _ => SwitchView(null);
        bar.AddChild(_galaxy); bar.AddChild(_title);
        var minus = SWConsoleStyle.TabButton("−"); minus.OnPressed += _ => _map.ChangeZoom(1 / 1.2f); bar.AddChild(minus);
        var plus = SWConsoleStyle.TabButton("+"); plus.OnPressed += _ => _map.ChangeZoom(1.2f); bar.AddChild(plus);
        bar.AddChild(_zoom);
        var reset = SWConsoleStyle.TabButton("Сбросить масштаб"); reset.OnPressed += _ => _map.ResetView(); bar.AddChild(reset);
        AddChild(bar);
        _scroll.AddChild(_details);
        AddChild(new SWAtlasLayout(_host, _scroll, stackBelow: 520, minDetails: 230, maxDetails: 330));
        SwitchView(null);
    }

    public void SetState(SWJusticeInspectionState? state)
    {
        _state = state;
        var revision = state == null ? "" : $"{state.Budget}:{state.CaseCap}:{state.CheckCap}:{state.BusyChecks}:{state.Workload}:{state.WorkloadStatus}:{state.ActiveCases.Count}:" +
            string.Join("|", state.Institutions.Select(i => $"{i.Id}:{i.Name}:{i.WorldId}:{i.KindName}:{i.Available}:{i.Reason}")) + "|" +
            string.Join("|", state.ActiveCases.Select(c => $"{c.Id}:{c.InstitutionId}:{c.Title}"));
        if (_revision == revision) return;
        _revision = revision;
        Refresh();
    }

    public void SelectWorld(string id)
    {
        var world = _source.Worlds.FirstOrDefault(w => w.Id == id);
        if (world == null) return;
        if (_sector != null && _sector != world.Sector?.Id || _sector == null && !world.Anchor) SwitchView(world.Sector?.Id);
        _world = id; _map.SelectWorld(id, true); Refresh();
    }

    private void SwitchView(string? sector)
    {
        if (_map != null && sector == _sector) return;
        var previous = _map;
        var anchor = _source.Worlds.FirstOrDefault(w => w.Anchor && w.Sector?.Id == (sector ?? _sector))?.Id;
        _sector = sector;
        var view = sector == null ? SWGalacticMapViews.Overview(_source) : SWGalacticMapViews.Sector(_source, sector);
        _host.RemoveAllChildren();
        _map = new SWGalacticMapControl(view, sector) { MinHeight = 260 };
        _map.WorldSelected += id => { _world = id; Refresh(); };
        _map.WorldActivated += id => { var world = _source.Worlds.FirstOrDefault(w => w.Id == id); if (world?.Sector is { } s) { SwitchView(s.Id); _world = id; _map.SelectWorld(id); Refresh(); } };
        _map.ViewChanged += () => _zoom.Text = $"{_map.Zoom * 100:0}%";
        _host.AddChild(_map);
        _galaxy.Visible = sector != null;
        _title.Text = sector == null ? "Галактика" : _source.Worlds.FirstOrDefault(w => w.Anchor && w.Sector?.Id == sector)?.Name ?? "Сектор";
        if (_world != null) _map.SelectWorld(_world);
        if (previous != null && anchor != null) _map.TransitionFrom(previous, anchor, sector != null);
    }

    private void Refresh()
    {
        var scroll = _scroll.VScroll;
        _details.RemoveAllChildren();
        _details.AddChild(SWConsoleStyle.Section("Учреждения"));
        if (_state == null) { _details.AddChild(SWConsoleStyle.Wrap("Нет данных о проверках", SWConsoleStyle.TextDim)); return; }
        _details.AddChild(SWConsoleStyle.Wrap(_world == null ? "Выберите мир на карте" : _source.Worlds.FirstOrDefault(w => w.Id == _world)?.Name ?? "Неизвестный мир", SWConsoleStyle.TextBright));
        var institutions = _state.Institutions.Where(i => i.WorldId == _world).ToList();
        if (_world != null && institutions.Count == 0) _details.AddChild(SWConsoleStyle.Wrap("Учреждений для проверки нет", SWConsoleStyle.TextDim));
        foreach (var institution in institutions)
        {
            _details.AddChild(SWConsoleStyle.Wrap(institution.Name, SWConsoleStyle.TextBright));
            _details.AddChild(SWConsoleStyle.Wrap(institution.KindName, SWConsoleStyle.TextDim));
            var active = _state.ActiveCases.FirstOrDefault(c => c.InstitutionId == institution.Id);
            if (active != null)
            {
                var dossier = SWConsoleStyle.TabButton("Перейти к делу");
                dossier.OnPressed += _ => CaseSelected?.Invoke(active.Id);
                _details.AddChild(dossier);
            }
            var open = SWConsoleStyle.Action("Открыть проверку", SWConsoleStyle.Accent, !institution.Available);
            if (institution.Available) open.OnPressed += _ => { OpenRequested?.Invoke(institution.Id); open.Disabled = true; };
            _details.AddChild(open);
            if (!institution.Available && !string.IsNullOrWhiteSpace(institution.Reason)) _details.AddChild(SWConsoleStyle.Wrap(institution.Reason, SWConsoleStyle.Warn));
        }
        _details.AddChild(SWConsoleStyle.Section("Открытые дела"));
        foreach (var active in _state.ActiveCases)
        {
            _details.AddChild(SWConsoleStyle.Wrap($"{active.Title}\n{active.WorldName}, {active.InstitutionName}", SWConsoleStyle.TextBright));
            var actions = new WrapContainer { HorizontalExpand = true, SeparationOverride = 4, CrossSeparationOverride = 4 };
            var focus = SWConsoleStyle.TabButton("На карте");
            focus.OnPressed += _ => SelectWorld(active.WorldId); actions.AddChild(focus);
            var dossier = SWConsoleStyle.TabButton("Перейти к делу");
            dossier.OnPressed += _ => CaseSelected?.Invoke(active.Id); actions.AddChild(dossier);
            _details.AddChild(actions);
        }
        _details.AddChild(SWConsoleStyle.Wrap($"Бюджет: {_state.Budget:N0} кр\nДела: {_state.ActiveCases.Count}/{_state.CaseCap}\nПроверки: {_state.BusyChecks}/{_state.CheckCap}\nНагрузка: {_state.Workload}/100, {_state.WorkloadStatus}", SWConsoleStyle.Brass));
        _scroll.VScroll = scroll;
    }
}

public sealed class SWJusticeDossiers : BoxContainer
{
    private readonly bool _archive;
    private readonly SWConsoleDocuments _documents = new("Поиск по делам", "Дел");
    private readonly RichTextLabel _budget = SWConsoleStyle.Wrap("", SWConsoleStyle.Brass);
    private readonly Dictionary<int, HashSet<string>> _selected = new();
    private readonly HashSet<string> _expanded = new();
    private readonly Dictionary<int, SWJusticeConclusion> _conclusions = new();
    private int _localRevision;
    private SWJusticeInspectionState? _state;
    public event Action<int, string>? CheckRequested;
    public event Action<int, SWJusticeConclusion, List<string>>? ConcludeRequested;
    public event Action<int, SWJusticeRemedy>? RemedyRequested;
    public event Action<int>? FollowUpRequested;

    public SWJusticeDossiers(bool archive)
    {
        _archive = archive; Orientation = LayoutOrientation.Vertical; HorizontalExpand = VerticalExpand = true;
        AddChild(_documents); AddChild(_budget);
    }
    public void SelectCase(int id) => _documents.SelectDocument(id);
    public void SetMaterialExpanded(int caseId, string checkId, bool expanded)
    {
        var key = $"{caseId}:{checkId}";
        if (expanded) _expanded.Add(key); else _expanded.Remove(key);
        Rebuild();
    }
    public void SetState(SWJusticeInspectionState? state)
    {
        _state = state;
        _budget.SetMessage(state == null ? "" : $"Бюджет: {state.Budget:N0} кр   Дела: {state.ActiveCases.Count}/{state.CaseCap}   Проверки: {state.BusyChecks}/{state.CheckCap}   Нагрузка: {state.Workload}/100, {state.WorkloadStatus}");
        var cases = state == null ? new List<SWJusticeCaseView>() : _archive ? state.Archive : state.ActiveCases;
        _documents.SetDocuments(cases.Select(c => new SWConsoleDocumentEntry(c.Id, c.Title, $"{c.WorldName}  {c.InstitutionName}", Revision(c, state!) + $"|{_localRevision}", () => Build(c))));
    }
    private static string Revision(SWJusticeCaseView c, SWJusticeInspectionState state) =>
        $"{c.Title}|{c.Briefing}|{c.Stage}|{c.SelectedConclusion}|{c.SelectedRemedy}|{c.CanConclude}|{c.CanFollowUp}|{c.Result}|{c.Explanation}|{c.ActualConclusion}|{SWSenateDuration.FormatHours(c.RemedyEtaHours)}|" +
        string.Join(";", c.Evidence.Select(e => $"{e.CheckId}:{e.Title}:{e.Text}")) + "|" +
        string.Join(";", c.Checks.Select(k => $"{k.Id}:{k.Title}:{k.Cost}:{k.Hours}:{k.Available}:{k.Reason}:{k.Pending}:{k.Completed}:{SWSenateDuration.FormatHours(k.EtaHours)}")) + "|" +
        string.Join(";", c.RemedyOptions.Select(r => $"{r.Remedy}:{r.Cost}:{r.Hours}:{r.Available}:{r.Reason}")) + "|" + string.Join(",", c.CitedEvidenceIds);

    private Control Build(SWJusticeCaseView c)
    {
        var body = SWConsoleDocument.Body(c.Title, c.Briefing);
        body.AddChild(SWConsoleStyle.Wrap($"{c.InstitutionName}\n{c.WorldName}, {c.SectorName}", SWConsoleStyle.TextDim));
        if (_archive)
        {
            body.AddChild(SWConsoleStyle.Section("Результат"));
            body.AddChild(SWConsoleStyle.Wrap(c.Result, SWConsoleStyle.TextBright));
            if (c.SelectedConclusion is { } selected) body.AddChild(SWConsoleStyle.Wrap($"Заключение: {ConclusionName(selected)}", SWConsoleStyle.TextBright));
            if (c.ActualConclusion is { } actual) body.AddChild(SWConsoleStyle.Wrap($"Установлено: {ConclusionName(actual)}", SWConsoleStyle.TextBright));
            body.AddChild(SWConsoleStyle.Wrap(c.Explanation, SWConsoleStyle.TextBright));
        }
        else if (c.Stage == SWJusticeInspectionStage.Inspecting)
        {
            body.AddChild(SWConsoleStyle.Section("Проверки"));
            foreach (var check in c.Checks)
            {
                var status = check.Completed ? "Завершена" : check.Pending ? $"Выполняется: {SWSenateDuration.FormatHours(check.EtaHours)}" : $"{check.Cost:N0} кр, {SWSenateDuration.FormatHours(check.Hours)}";
                body.AddChild(SWConsoleStyle.Wrap($"{check.Title}\n{status}", SWConsoleStyle.TextBright));
                if (!check.Completed && !check.Pending)
                {
                    var run = SWConsoleStyle.Action("Провести проверку", SWConsoleStyle.Accent, !check.Available);
                    if (check.Available) run.OnPressed += _ => { CheckRequested?.Invoke(c.Id, check.Id); run.Disabled = true; };
                    body.AddChild(run);
                    if (!check.Available && !string.IsNullOrEmpty(check.Reason)) body.AddChild(SWConsoleStyle.Wrap(check.Reason, SWConsoleStyle.Warn));
                }
            }
        }
        body.AddChild(SWConsoleStyle.Section("Материалы"));
        if (!_selected.TryGetValue(c.Id, out var selectedEvidence)) _selected[c.Id] = selectedEvidence = new HashSet<string>();
        foreach (var evidence in c.Evidence)
        {
            var key = $"{c.Id}:{evidence.CheckId}";
            var expand = SWConsoleStyle.TabButton((_expanded.Contains(key) ? "Скрыть: " : "Открыть: ") + evidence.Title);
            expand.OnPressed += _ => SetMaterialExpanded(c.Id, evidence.CheckId, !_expanded.Contains(key));
            body.AddChild(expand);
            if (c.CitedEvidenceIds.Contains(evidence.CheckId)) body.AddChild(SWConsoleStyle.Wrap("Использовано в заключении", SWConsoleStyle.TextDim));
            if (_expanded.Contains(key)) body.AddChild(SWConsoleStyle.Wrap(evidence.Text, SWConsoleStyle.TextBright));
            if (!_archive && c.Stage == SWJusticeInspectionStage.Inspecting)
            {
                var checkbox = new CheckBox { Text = "Использовать в заключении", Pressed = selectedEvidence.Contains(evidence.CheckId) };
                checkbox.OnToggled += args => { if (args.Pressed) selectedEvidence.Add(evidence.CheckId); else selectedEvidence.Remove(evidence.CheckId); Rebuild(); };
                body.AddChild(checkbox);
            }
        }
        if (!_archive && c.Stage == SWJusticeInspectionStage.Inspecting)
        {
            body.AddChild(SWConsoleStyle.Section("Заключение"));
            var choices = new WrapContainer { HorizontalExpand = true, SeparationOverride = 4, CrossSeparationOverride = 4 };
            foreach (var conclusion in new[] { SWJusticeConclusion.NoViolation, SWJusticeConclusion.Negligence, SWJusticeConclusion.IntentionalViolation })
            {
                var button = SWConsoleStyle.TabButton(ConclusionName(conclusion));
                SWConsoleStyle.StyleTabButton(button, _conclusions.TryGetValue(c.Id, out var choice) && choice == conclusion);
                button.OnPressed += _ => { _conclusions[c.Id] = conclusion; Rebuild(); }; choices.AddChild(button);
            }
            body.AddChild(choices);
            var canSubmit = c.CanConclude && selectedEvidence.Count >= 2 && _conclusions.ContainsKey(c.Id);
            var submit = SWConsoleStyle.Primary("Подписать заключение", !canSubmit);
            if (canSubmit) submit.OnPressed += _ => { ConcludeRequested?.Invoke(c.Id, _conclusions[c.Id], selectedEvidence.ToList()); submit.Disabled = true; };
            body.AddChild(submit);
            if (!canSubmit)
            {
                var reason = !_conclusions.ContainsKey(c.Id) ? "Не выбрано заключение" : selectedEvidence.Count < 2 ? "Нужно выбрать два материала" :
                    c.Checks.Any(check => check.Pending) ? "Дождитесь окончания проверки" : "Ведомство не работает";
                body.AddChild(SWConsoleStyle.Wrap(reason, SWConsoleStyle.TextDim));
            }
        }
        else if (!_archive && c.Stage == SWJusticeInspectionStage.Enforcement)
        {
            if (c.SelectedConclusion is { } conclusion) body.AddChild(SWConsoleStyle.Wrap($"Заключение: {ConclusionName(conclusion)}", SWConsoleStyle.TextBright));
            body.AddChild(SWConsoleStyle.Section("Мера"));
            if (c.SelectedRemedy == null)
            {
                if (c.RemedyOptions.Count == 0) body.AddChild(SWConsoleStyle.Wrap("Меры недоступны, пока министерство не работает", SWConsoleStyle.TextDim));
                foreach (var option in c.RemedyOptions)
                {
                    body.AddChild(SWConsoleStyle.Wrap($"{RemedyName(option.Remedy)}: {option.Cost:N0} кр, {SWSenateDuration.FormatHours(option.Hours)}", SWConsoleStyle.TextBright));
                    var action = SWConsoleStyle.Action("Применить меру", SWConsoleStyle.Accent, !option.Available);
                    if (option.Available) action.OnPressed += _ => { RemedyRequested?.Invoke(c.Id, option.Remedy); action.Disabled = true; };
                    body.AddChild(action);
                    if (!option.Available && !string.IsNullOrWhiteSpace(option.Reason)) body.AddChild(SWConsoleStyle.Wrap(option.Reason, SWConsoleStyle.Warn));
                }
            }
            else
            {
                body.AddChild(SWConsoleStyle.Wrap(RemedyName(c.SelectedRemedy.Value), SWConsoleStyle.TextBright));
                var follow = SWConsoleStyle.Action("Проверить исполнение", SWConsoleStyle.Accent, !c.CanFollowUp);
                if (c.CanFollowUp) follow.OnPressed += _ => { FollowUpRequested?.Invoke(c.Id); follow.Disabled = true; };
                body.AddChild(follow);
                if (!c.CanFollowUp) body.AddChild(SWConsoleStyle.Wrap(c.RemedyEtaHours > 0 ? $"Ожидание: {SWSenateDuration.FormatHours(c.RemedyEtaHours)}" : "Проверка исполнения недоступна, пока министерство не работает", SWConsoleStyle.TextDim));
            }
        }
        return body;
    }
    private void Rebuild() { _localRevision++; SetState(_state); }
    private static string ConclusionName(SWJusticeConclusion value) => value switch { SWJusticeConclusion.NoViolation => "Нарушений нет", SWJusticeConclusion.Negligence => "Халатность", _ => "Умышленное нарушение" };
    private static string RemedyName(SWJusticeRemedy value) => value switch { SWJusticeRemedy.Close => "Закрыть проверку", SWJusticeRemedy.CorrectRecords => "Исправить документы", SWJusticeRemedy.Restitution => "Возместить ущерб", _ => "Ограничить работу" };
}
