using System;
using System.Collections.Generic;
using System.Linq;
using Content.Client._SW._Senate.Consoles.Statistics;
using Content.Shared._SW._Senate;
using Robust.Client.UserInterface;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client._SW._Senate.Consoles.MinistryWork;

public sealed partial class SWMinistryWorkConsoleMenu
{
    private string? _budgetEditorKey;
    private int _budgetEditorRevision;
    private Button? _reserveTransfer;
    private int _reserveTransferRevision = -1;
    private string? _reserveRequestId;
    private RichTextLabel? _reserveResult;
    private LineEdit _sectorEdit = default!;
    private LineEdit _reserveEdit = default!;
    private LineEdit _ministryEdit = default!;
    private readonly Dictionary<string, LineEdit> _weightEdits = new();
    private RichTextLabel _budgetInfo = default!;
    private RichTextLabel _budgetError = default!;
    private Button _budgetReload = default!;
    private string? _reserveKey;
    private RichTextLabel _reserveBalance = default!;
    private BoxContainer _reserveHistory = default!;
    private BoxContainer _reserveTransfersPanel = default!;
    private BoxContainer _reserveRequestsPanel = default!;
    private BoxContainer _reserveRequestsList = default!;
    private RichTextLabel _governorResult = default!;
    private string? _governorKey;
    private string? _governorPending;
    private bool _showGovernorRequests = true;
    private SWRepublicMapBrowser? _financeMap;
    private string? _financeMapSignature;
    private RichTextLabel _draftSummary = default!;
    private readonly Dictionary<string, Label> _draftAmounts = new();
    private PanelContainer[] _splitBars = Array.Empty<PanelContainer>();

    private void BuildBudgets(SWMinistryWorkConsoleState state)
    {
        if (!state.IsFinance) return;
        var key = $"{state.BudgetDay}:{state.BudgetPhaseLabel}:{state.BudgetCanEdit}:" + string.Join(",", state.BudgetShares.Select(s => s.MinistryId));
        if (_budgetEditorKey != key)
        {
            _budgetEditorKey = key;
            _budgetEditorRevision = state.BudgetRevision;
            BudgetsContent.RemoveAllChildren();
            _weightEdits.Clear();
            _draftAmounts.Clear();
            _budgetInfo = SWConsoleStyle.Wrap("", SWConsoleStyle.TextBright);
            BudgetsContent.AddChild(_budgetInfo);
            _budgetError = SWConsoleStyle.Wrap("", SWConsoleStyle.Bad);
            BudgetsContent.AddChild(_budgetError);
            _budgetReload = SWConsoleStyle.Action("Загрузить актуальный проект", SWConsoleStyle.Neutral);
            _budgetReload.OnPressed += _ => { _budgetEditorKey = null; if (_state != null) BuildBudgets(_state); };
            BudgetsContent.AddChild(_budgetReload);
            _draftSummary = SWConsoleStyle.Wrap("", SWConsoleStyle.TextBright);
            BudgetsContent.AddChild(_draftSummary);
            var bar = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true, MinHeight = 10 };
            _splitBars = new[] { SWConsoleStyle.Accent, SWConsoleStyle.Good, SWConsoleStyle.Brass }.Select(color => new PanelContainer
            { HorizontalExpand = true, MinHeight = 10, PanelOverride = new StyleBoxFlat { BackgroundColor = color } }).ToArray();
            foreach (var segment in _splitBars) bar.AddChild(segment);
            BudgetsContent.AddChild(bar);
            var split = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 8 };
            split.AddChild(SWConsoleStyle.Text("Сектора, %", SWConsoleStyle.Accent, SWConsoleStyle.Body));
            _sectorEdit = new LineEdit { Text = state.BudgetSectorPct.ToString(), MinWidth = 55, Editable = state.BudgetCanEdit };
            split.AddChild(_sectorEdit);
            split.AddChild(SWConsoleStyle.Text("Ведомства, %", SWConsoleStyle.Good, SWConsoleStyle.Body));
            _ministryEdit = new LineEdit { Text = (100 - state.BudgetSectorPct - state.BudgetReservePct).ToString(), MinWidth = 55, Editable = state.BudgetCanEdit };
            split.AddChild(_ministryEdit);
            split.AddChild(SWConsoleStyle.Text("Резерв, %", SWConsoleStyle.Brass, SWConsoleStyle.Body));
            _reserveEdit = new LineEdit { Text = state.BudgetReservePct.ToString(), MinWidth = 55, Editable = state.BudgetCanEdit };
            split.AddChild(_reserveEdit);
            BudgetsContent.AddChild(split);
            _sectorEdit.OnTextChanged += _ => SyncMinistryShare();
            _reserveEdit.OnTextChanged += _ => SyncMinistryShare();
            _ministryEdit.OnTextChanged += _ =>
            {
                if (int.TryParse(_ministryEdit.Text, out var ministry) && int.TryParse(_reserveEdit.Text, out var reserve) && ministry >= 0 && reserve >= 0 && ministry + reserve <= 100)
                    _sectorEdit.SetText((100 - ministry - reserve).ToString());
                UpdateFinanceDraft();
            };
            BudgetsContent.AddChild(SWConsoleStyle.Section("Доли ведомств"));
            foreach (var share in state.BudgetShares)
            {
                var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 6 };
                row.AddChild(SWConsoleStyle.Text(share.Name, share.Color, SWConsoleStyle.Body, 220));
                row.AddChild(SWConsoleStyle.Text("Вес", SWConsoleStyle.TextDim, SWConsoleStyle.Small));
                var weight = new LineEdit { Text = share.Weight.ToString(), MinWidth = 55, Editable = state.BudgetCanEdit };
                _weightEdits[share.MinistryId] = weight;
                row.AddChild(weight);
                var amount = SWConsoleStyle.Text("", SWConsoleStyle.TextBright, SWConsoleStyle.Body);
                _draftAmounts[share.MinistryId] = amount;
                row.AddChild(amount);
                weight.OnTextChanged += _ => UpdateFinanceDraft();
                BudgetsContent.AddChild(row);
            }
            if (state.BudgetCanEdit)
            {
                var submit = SWConsoleStyle.Action("Подать проект канцлеру", SWConsoleStyle.Good);
                submit.OnPressed += _ => SubmitFinanceBudget();
                BudgetsContent.AddChild(submit);
            }
        }
        _budgetInfo.SetMessage($"{state.BudgetPhaseLabel}   Бюджет дня: {state.BudgetDayTotal:N0} кр" +
            (string.IsNullOrWhiteSpace(state.BudgetRejectReason) ? "" : $"\nЗамечание канцлера: {state.BudgetRejectReason}") +
            (state.BudgetAutoApplied ? $"\nПрименено стандартное распределение: {state.BudgetAutoReason}" : ""));
        _budgetReload.Visible = state.BudgetCanEdit && _budgetEditorRevision != state.BudgetRevision;
        if (_budgetReload.Visible)
            _budgetError.SetMessage("Проект изменился на сервере. Загрузите актуальную версию перед отправкой; местные правки будут сброшены.");
        UpdateFinanceDraft();
    }


    private void SyncMinistryShare()
    {
        if (int.TryParse(_sectorEdit.Text, out var sector) && int.TryParse(_reserveEdit.Text, out var reserve) && sector >= 0 && reserve >= 0 && sector + reserve <= 100)
            _ministryEdit.SetText((100 - sector - reserve).ToString());
        UpdateFinanceDraft();
    }

    private void UpdateFinanceDraft()
    {
        var state = _state;
        if (state == null || !int.TryParse(_sectorEdit.Text, out var sector) || !int.TryParse(_reserveEdit.Text, out var reserve) || sector < 0 || reserve < 0 || sector + reserve > 100)
        { _draftSummary.SetMessage("Проверьте доли: сумма не должна превышать 100%."); return; }
        if (!int.TryParse(_ministryEdit.Text, out var enteredMinistry) || enteredMinistry < 0 || sector + reserve + enteredMinistry != 100)
        { _draftSummary.SetMessage("Сумма долей должна составлять 100%."); return; }
        var ministry = 100 - sector - reserve;
        var sectorPool = (int) MathF.Round(state.BudgetDayTotal * sector / 100f);
        var reservePool = (int) MathF.Round(state.BudgetDayTotal * reserve / 100f);
        var ministryPool = state.BudgetDayTotal - sectorPool - reservePool;
        var weights = _weightEdits.ToDictionary(x => x.Key, x => int.TryParse(x.Value.Text, out var w) && w >= 0 && w <= 6 ? w : 0);
        var totalWeight = weights.Values.Sum();
        _draftSummary.SetMessage($"Сектора {sector}% — {sectorPool:N0} кр\nВедомства {ministry}% — {ministryPool:N0} кр\nРезерв {reserve}% — {reservePool:N0} кр");
        var values = new[] { sector, ministry, reserve };
        for (var i = 0; i < values.Length; i++) { _splitBars[i].Visible = values[i] > 0; _splitBars[i].SizeFlagsStretchRatio = Math.Max(1, values[i]); }
        foreach (var (id, label) in _draftAmounts)
            label.Text = totalWeight == 0 ? "—" : $"≈ {(int) MathF.Round((float) ministryPool * weights[id] / totalWeight):N0} кр";
    }

    private void SubmitFinanceBudget()
    {
        var state = _state;
        if (state != null && _budgetEditorRevision != state.BudgetRevision)
        { _budgetError.SetMessage("Проект изменился на сервере. Загрузите актуальную версию."); return; }
        if (state == null || !state.BudgetCanSubmit) { _budgetError.SetMessage("Сейчас проект нельзя подать."); return; }
        if (!int.TryParse(_sectorEdit.Text, out var sector) || !int.TryParse(_reserveEdit.Text, out var reserve) || sector < 0 || reserve < 0 || sector + reserve > 100)
        { _budgetError.SetMessage("Введите доли от 0 до 100%; их сумма не должна превышать 100%."); return; }
        if (!int.TryParse(_ministryEdit.Text, out var ministry) || ministry < 0 || sector + reserve + ministry != 100)
        { _budgetError.SetMessage("Сумма долей должна составлять 100%."); return; }
        var weights = new Dictionary<string, int>();
        foreach (var (id, edit) in _weightEdits)
        {
            if (!int.TryParse(edit.Text, out var weight) || weight < 0 || weight > 6)
            { _budgetError.SetMessage("Доля каждого ведомства должна быть целым числом от 0 до 6."); return; }
            weights[id] = weight;
        }
        if (weights.Count > 0 && weights.Values.Sum() == 0 && sector + reserve < 100)
        { _budgetError.SetMessage("Укажите ненулевую долю хотя бы одного ведомства."); return; }
        _budgetError.SetMessage("");
        _owner.SubmitBudget(_budgetEditorRevision, sector, reserve, weights);
    }

    private void BuildFinanceReserve(SWMinistryWorkConsoleState state)
    {
        if (!state.IsFinance) return;
        var key = string.Join("|", state.BudgetTransferTargets.Select(t => $"{t.Ministry}:{t.Id}:{t.Name}"));
        if (_reserveKey != key)
        {
            _reserveKey = key;
            ReservePanel.RemoveAllChildren();
            _reserveBalance = SWConsoleStyle.Wrap("", SWConsoleStyle.Brass);
            ReservePanel.AddChild(_reserveBalance);
            var tabs = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 6 };
            var requestsTab = SWConsoleStyle.TabButton("Запросы секторов");
            var transfersTab = SWConsoleStyle.TabButton("Переводы и история");
            tabs.AddChild(requestsTab); tabs.AddChild(transfersTab); ReservePanel.AddChild(tabs);
            _reserveRequestsPanel = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, VerticalExpand = true };
            _reserveTransfersPanel = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, VerticalExpand = true };
            ReservePanel.AddChild(_reserveRequestsPanel); ReservePanel.AddChild(_reserveTransfersPanel);
            void SwitchReserveTab(bool requests)
            {
                _showGovernorRequests = requests;
                _reserveRequestsPanel.Visible = requests;
                _reserveTransfersPanel.Visible = !requests;
                SWConsoleStyle.StyleTabButton(requestsTab, requests);
                SWConsoleStyle.StyleTabButton(transfersTab, !requests);
            }
            requestsTab.OnPressed += _ => SwitchReserveTab(true);
            transfersTab.OnPressed += _ => SwitchReserveTab(false);
            _governorResult = SWConsoleStyle.Wrap("", SWConsoleStyle.TextBright);
            _reserveRequestsPanel.AddChild(_governorResult);
            var requestScroll = new ScrollContainer { HorizontalExpand = true, VerticalExpand = true, HScrollEnabled = false };
            _reserveRequestsList = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true };
            requestScroll.AddChild(_reserveRequestsList); _reserveRequestsPanel.AddChild(requestScroll);
            _governorKey = null;
            SwitchReserveTab(_showGovernorRequests);
            var target = new OptionButton { Filterable = true, HorizontalExpand = true };
            for (var i = 0; i < state.BudgetTransferTargets.Count; i++) target.AddItem(state.BudgetTransferTargets[i].Name, i);
            _reserveTransfersPanel.AddChild(target);
            var amount = new LineEdit { PlaceHolder = "Сумма, кр", HorizontalExpand = true };
            var purpose = new LineEdit { PlaceHolder = "Назначение перевода", HorizontalExpand = true };
            _reserveTransfersPanel.AddChild(amount); _reserveTransfersPanel.AddChild(purpose);
            var error = SWConsoleStyle.Wrap("", SWConsoleStyle.Bad);
            _reserveResult = error;
            _reserveTransfersPanel.AddChild(error);
            var transfer = SWConsoleStyle.Action("Перевести из резерва", SWConsoleStyle.Good);
            _reserveTransfer = transfer;
            transfer.OnPressed += _ =>
            {
                var current = _state;
                if (current == null || !current.BudgetCanTransfer || target.SelectedId < 0 || target.SelectedId >= current.BudgetTransferTargets.Count || !int.TryParse(amount.Text, out var value) || value <= 0 || value > current.BudgetReserveRemaining || string.IsNullOrWhiteSpace(purpose.Text))
                { error.SetMessage("Выберите адресата, укажите назначение и сумму в пределах остатка."); return; }
                var selected = current.BudgetTransferTargets[target.SelectedId];
                transfer.Disabled = true;
                _reserveTransferRevision = current.BudgetRevision;
                _reserveRequestId = Guid.NewGuid().ToString();
                _owner.TransferReserve(current.BudgetRevision, _reserveRequestId, selected.Ministry, selected.Id, value, purpose.Text.Trim());
                error.SetMessage("");
            };
            _reserveTransfersPanel.AddChild(transfer);
            _reserveTransfersPanel.AddChild(SWConsoleStyle.Section("История переводов"));
            var scroll = new ScrollContainer { HorizontalExpand = true, VerticalExpand = true, HScrollEnabled = false };
            _reserveHistory = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true };
            scroll.AddChild(_reserveHistory); _reserveTransfersPanel.AddChild(scroll);
        }
        if (_reserveTransfer != null && _reserveTransferRevision != state.BudgetRevision) _reserveTransfer.Disabled = !state.BudgetCanTransfer;
        if (_reserveRequestId != null && state.BudgetTransferResultRequestId == _reserveRequestId)
            _reserveResult?.SetMessage(state.BudgetTransferResult);
        BuildGovernorRequests(state);
        _reserveBalance.SetMessage($"Остаток резерва: {state.BudgetReserveRemaining:N0} кр из {state.BudgetReservePool:N0} кр\nОстаток возвращается в общую казну в конце дня.");
        _reserveHistory.RemoveAllChildren();
        foreach (var entry in state.BudgetTransferHistory)
            _reserveHistory.AddChild(SWConsoleStyle.Wrap($"{entry.Time}   {entry.TargetName}   {entry.Amount:N0} кр\n{entry.Purpose}", SWConsoleStyle.TextBright));
    }

    private void BuildGovernorRequests(SWMinistryWorkConsoleState state)
    {
        if (_governorPending != null && _governorPending == state.FinanceGovernorResultRequestId)
        {
            _governorResult.SetMessage(state.FinanceGovernorResult);
            _governorPending = null;
            _governorKey = null;
        }
        var key = string.Join("|", state.FinanceGovernorRequests.Select(r => $"{r.RequestId}:{r.SectorName}:{r.WorldName}:{r.Governor}:{r.ProjectName}:{r.Outcome}:{r.Reason}:{r.Cost}:{r.Missing}")) + $"/{state.BudgetCanTransfer}/{state.BudgetReserveRemaining}/{state.FinanceGovernorResult}";
        if (_governorKey == key) return;
        _governorKey = key;
        _reserveRequestsList.RemoveAllChildren();
        if (state.FinanceGovernorRequests.Count == 0)
            _reserveRequestsList.AddChild(SWConsoleStyle.Wrap("Нет запросов секторов", SWConsoleStyle.TextDim));
        foreach (var request in state.FinanceGovernorRequests)
        {
            var panel = SWConsoleStyle.Row(SWConsoleStyle.BgPanel, SWConsoleStyle.Brass);
            var card = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true };
            card.AddChild(SWConsoleStyle.Wrap($"{request.SectorName}   {request.WorldName}\nГубернатор: {request.Governor}", SWConsoleStyle.TextBright));
            card.AddChild(SWConsoleStyle.Wrap($"{request.ProjectName}\n{request.Outcome}\n{request.Reason}\nСтоимость {request.Cost:N0} кр   Средства сектора {Math.Max(0, request.Cost - request.Missing):N0} кр   Из резерва {request.Missing:N0} кр", SWConsoleStyle.TextBright));
            var actions = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 6 };
            var approve = SWConsoleStyle.Action("Выделить средства", SWConsoleStyle.Good);
            approve.Disabled = !state.BudgetCanTransfer || request.Missing > state.BudgetReserveRemaining;
            if (approve.Disabled) approve.ToolTip = state.BudgetCanTransfer ? "Недостаточно средств резерва" : "Резерв ещё недоступен";
            var reject = SWConsoleStyle.Action("Отказать", SWConsoleStyle.Bad);
            approve.OnPressed += _ => { approve.Disabled = true; reject.Disabled = true; _governorPending = request.RequestId; _governorResult.SetMessage("Ожидается ответ сервера"); _owner.DecideGovernorRequest(request.RequestId, true); };
            reject.OnPressed += _ => { approve.Disabled = true; reject.Disabled = true; _governorPending = request.RequestId; _governorResult.SetMessage("Ожидается ответ сервера"); _owner.DecideGovernorRequest(request.RequestId, false); };
            actions.AddChild(approve); actions.AddChild(reject); card.AddChild(actions); panel.AddChild(card);
            _reserveRequestsList.AddChild(panel);
        }
    }

    private void BuildFinanceMap(SWMinistryWorkConsoleState state)
    {
        if (!state.IsFinance) return;
        if (_financeMap == null)
        {
            FinanceMapPanel.AddChild(SWConsoleStyle.Wrap("Поступления с начала дня\nЗелёный — без выявленных проблем\nЖёлтый — обнаружена проблема\nСерый — нет данных", SWConsoleStyle.TextDim));
            _financeMap = new SWRepublicMapBrowser(financeOnly: true) { EconomyDetailsOverride = FinanceMapSelection };
            FinanceMapPanel.AddChild(_financeMap);
        }
        var signature = string.Join("|", state.FinanceRevenueSectors.Select(x => $"{x.SectorId}:{x.ProducedValue}:{x.Tax}:{string.Join(",", x.Reasons)}"));
        if (_financeMapSignature != signature)
        {
            _financeMapSignature = signature;
            _financeMap.SetFinanceSectors(state.FinanceRevenueSectors);
            _financeMap.RefreshEconomySelection();
        }
    }

    private void FinanceMapSelection(BoxContainer heading, BoxContainer details, string? world, Content.Shared._SW._Senate.Sectors.SWGalacticMapRoute? route)
    {
        var sectorId = _financeMap?.SelectedSectorId ?? _financeMap?.CurrentSectorId;
        var sector = _state?.FinanceRevenueSectors.FirstOrDefault(s => s.SectorId == sectorId);
        if (sector == null) { heading.AddChild(SWConsoleStyle.Wrap("Выберите сектор", SWConsoleStyle.TextDim)); return; }
        heading.AddChild(SWConsoleStyle.Section(sector.Name));
        details.AddChild(SWConsoleStyle.Text("Оценка выпуска с начала дня", SWConsoleStyle.TextDim, SWConsoleStyle.Small));
        details.AddChild(SWConsoleStyle.Text($"{sector.ProducedValue:N0} кр", SWConsoleStyle.TextBright, SWConsoleStyle.Strong));
        details.AddChild(SWConsoleStyle.Text("Налоговые поступления с начала дня", SWConsoleStyle.TextDim, SWConsoleStyle.Small));
        details.AddChild(SWConsoleStyle.Text($"{sector.Tax:N0} кр", SWConsoleStyle.TextBright, SWConsoleStyle.Strong));
        foreach (var reason in sector.Reasons) details.AddChild(SWConsoleStyle.Wrap(reason, SWConsoleStyle.TextDim));
    }
    private sealed class TaxEditor
    {
        public OptionButton Picker = default!;
        public LineEdit Rate = default!;
        public Button Apply = default!;
        public RichTextLabel Status = default!;
        public BoxContainer Active = default!;
        public string? Selected;
        public string CatalogKey = string.Empty;
        public string ActiveKey = string.Empty;
        public bool Relief;
        public string? RequestId;
        public string? SubmittedId;
        public string? SubmittedText;
    }
    private TaxEditor? _goodsEditor;
    private TaxEditor? _reliefEditor;

    private void BuildFinanceTaxes(SWMinistryWorkConsoleState state)
    {
        if (!state.IsFinance) return;
        if (_goodsEditor == null)
        {
            _goodsEditor = CreateTaxEditor("Дополнительные налоги", false);
            _reliefEditor = CreateTaxEditor("Отраслевые льготы", true);
        }
        UpdateTaxEditor(_goodsEditor, state.FinanceTaxGoods);
        UpdateTaxEditor(_reliefEditor!, state.FinanceReliefs);
        foreach (var editor in new[] { _goodsEditor, _reliefEditor! })
        {
            if (editor.RequestId != null && editor.RequestId == state.FinanceTaxResultRequestId)
            {
                var changed = editor.Selected != editor.SubmittedId || editor.Rate.Text != editor.SubmittedText;
                editor.Status.SetMessage(state.FinanceTaxResult + (changed ? "\nТекущая правка не сохранена" : ""));
                if (!changed && editor.Selected != null)
                {
                    var current = TaxEntries(editor).FirstOrDefault(e => e.Id == editor.Selected);
                    editor.Apply.Text = current is { Rate: > 0 } ? "Сохранить изменение" : editor.Relief ? "Ввести льготу" : "Ввести налог";
                }
                editor.RequestId = null;
            }
        }
    }

    private TaxEditor CreateTaxEditor(string title, bool relief)
    {
        var editor = new TaxEditor { Relief = relief };
        FinanceTaxesContent.AddChild(SWConsoleStyle.Section(title));
        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 6, HorizontalExpand = true };
        editor.Picker = new OptionButton { Name = relief ? "FinanceReliefPicker" : "FinanceTaxPicker", Filterable = true, HorizontalExpand = true, MinWidth = 180, Prefix = relief ? "Отрасль" : "Товар" };
        editor.Rate = new LineEdit { Name = relief ? "FinanceReliefRate" : "FinanceTaxRate", PlaceHolder = "Ставка %", MinWidth = 80 };
        editor.Apply = SWConsoleStyle.Action(relief ? "Ввести льготу" : "Ввести налог", SWConsoleStyle.Accent);
        editor.Apply.Name = relief ? "FinanceReliefApply" : "FinanceTaxApply";
        row.AddChild(editor.Picker); row.AddChild(editor.Rate); row.AddChild(editor.Apply);
        FinanceTaxesContent.AddChild(row);
        editor.Status = SWConsoleStyle.Wrap("", SWConsoleStyle.TextDim);
        FinanceTaxesContent.AddChild(editor.Status);
        editor.Active = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true };
        FinanceTaxesContent.AddChild(editor.Active);
        editor.Picker.OnItemSelected += args =>
        {
            var entries = TaxEntries(editor);
            if (args.Id <= 0 || args.Id > entries.Count) { editor.Selected = null; return; }
            var selected = entries[args.Id - 1];
            editor.Selected = selected.Id;
            editor.Rate.Text = selected.Rate.ToString();
            editor.Apply.Text = selected.Rate > 0 ? "Сохранить изменение" : relief ? "Ввести льготу" : "Ввести налог";
            editor.Status.SetMessage("");
        };
        editor.Rate.OnTextChanged += _ => editor.Status.SetMessage("Изменение не сохранено");
        editor.Apply.OnPressed += _ => SendTaxEditor(editor, false);
        return editor;
    }

    private List<SWFinanceTaxEntry> TaxEntries(TaxEditor editor) => _state == null ? new() : editor.Relief ? _state.FinanceReliefs : _state.FinanceTaxGoods;

    private void SendTaxEditor(TaxEditor editor, bool cancel, string? targetId = null)
    {
        var selectedId = targetId ?? editor.Selected;
        if (selectedId == null || !TaxEntries(editor).Any(e => e.Id == selectedId))
        { editor.Status.SetMessage(editor.Relief ? "Выберите отрасль." : "Выберите товар."); return; }
        if (!cancel && (!int.TryParse(editor.Rate.Text, out var parsed) || parsed < 0 || parsed > 100))
        { editor.Status.SetMessage("Введите ставку от 0 до 100%."); return; }
        var rate = cancel ? 0 : int.Parse(editor.Rate.Text);
        editor.RequestId = Guid.NewGuid().ToString();
        editor.SubmittedId = editor.Selected;
        editor.SubmittedText = editor.Rate.Text;
        editor.Status.SetMessage("Ожидается ответ сервера");
        _owner.SetFinanceTax(selectedId, rate, editor.Relief, editor.RequestId);
    }

    private void UpdateTaxEditor(TaxEditor editor, List<SWFinanceTaxEntry> entries)
    {
        var catalog = string.Join("|", entries.Select(e => $"{e.Id}:{e.Name}"));
        if (editor.CatalogKey != catalog)
        {
            editor.CatalogKey = catalog;
            editor.Picker.Clear();
            editor.Picker.AddItem(editor.Relief ? "Выберите отрасль" : "Выберите товар", 0);
            for (var i = 0; i < entries.Count; i++) editor.Picker.AddItem(entries[i].Name, i + 1);
            var selected = entries.FindIndex(e => e.Id == editor.Selected);
            editor.Picker.SelectId(selected + 1);
        }
        var active = string.Join("|", entries.Where(e => e.Rate > 0).Select(e => $"{e.Id}:{e.Name}:{e.Rate}"));
        if (editor.ActiveKey == active) return;
        editor.ActiveKey = active;
        editor.Active.RemoveAllChildren();
        foreach (var entry in entries.Where(e => e.Rate > 0))
        {
            var item = entry;
            var line = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 6, HorizontalExpand = true };
            line.AddChild(SWConsoleStyle.Text(item.Name, SWConsoleStyle.TextBright, SWConsoleStyle.Body, 220));
            line.AddChild(SWConsoleStyle.Text($"{item.Rate}%", SWConsoleStyle.Brass, SWConsoleStyle.Body, 50));
            var edit = SWConsoleStyle.Action("Изменить", SWConsoleStyle.Neutral);
            edit.Name = $"FinanceTaxEdit_{(editor.Relief ? "Relief" : "Good")}_{item.Id}";
            edit.OnPressed += _ =>
            {
                editor.Selected = item.Id;
                editor.Picker.SelectId(TaxEntries(editor).FindIndex(e => e.Id == item.Id) + 1);
                editor.Rate.Text = item.Rate.ToString();
                editor.Apply.Text = "Сохранить изменение";
                editor.Status.SetMessage("");
            };
            var cancel = SWConsoleStyle.Action("Отменить", SWConsoleStyle.Bad);
            cancel.Name = $"FinanceTaxCancel_{(editor.Relief ? "Relief" : "Good")}_{item.Id}";
            cancel.OnPressed += _ => SendTaxEditor(editor, true, item.Id);
            line.AddChild(edit); line.AddChild(cancel); editor.Active.AddChild(line);
        }
    }
}
