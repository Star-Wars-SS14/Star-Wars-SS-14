using System;
using System.Linq;
using System.Collections.Generic;
using System.Numerics;
using Content.Shared._SW._Senate.Ministries;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;
using Content.Shared.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.IoC;
using Robust.Shared.Timing;

namespace Content.Client._SW._Senate.Consoles.MinistryWork;

/// <summary>Persistent dossier navigation; all values and curves are published simulation data.</summary>
public sealed class SWHealthResearchPanel : BoxContainer
{
    private SWHealthView _state = new();
    private SWHealthStage? _selected;
    private string _source = "", _lab = "", _producer = "", _recipient = "";
    private int _batch = 1;
    private BoxContainer? _candidateCards;
    private SWHealthAnalysisDiagram? _analysisDiagram;
    private readonly List<Button> _stageButtons = new();
    private readonly List<OptionButton> _pickers = new();
    private readonly GridContainer _stages = new() { Columns = 6, HorizontalExpand = true, HSeparationOverride = 4, VSeparationOverride = 4 };
    private readonly BoxContainer _body = Stack();
    public event Action<SWHealthActionMessage>? ActionRequested;
    public SWHealthStage SelectedStage => _selected ?? _state.Research.Stage;
    public string SelectedRecipient => _recipient;
    private static readonly string[] Titles = { "Образцы", "Анализ", "Препарат", "Испытания", "Выпуск", "Вакцинация" };
    public SWHealthResearchPanel()
    {
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        SeparationOverride = 8;
        for (var i = 0; i < Titles.Length; i++)
        {
            var index = i;
            var button = Button($"{i + 1}  {Titles[i]}", () => { _selected = (SWHealthStage) index; Rebuild(); });
            button.Name = "HealthStage" + i;
            button.ToggleMode = true;
            _stageButtons.Add(button);
            _stages.AddChild(button);
        }
        AddChild(_stages);
        AddChild(_body);
    }
    protected override void Resized()
    {
        base.Resized();
        _stages.Columns = Width < 850 ? 3 : 6;
        if (_candidateCards != null) _candidateCards.Orientation = Width < 850 ? LayoutOrientation.Vertical : LayoutOrientation.Horizontal;
    }
    private static BoxContainer Stack() => new() { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, SeparationOverride = 6 };
    private static BoxContainer Row() => new() { Orientation = LayoutOrientation.Horizontal, HorizontalExpand = true, SeparationOverride = 4 };
    private void Text(string text, Color? color = null) => _body.AddChild(SWConsoleStyle.Wrap(text, color ?? SWConsoleStyle.TextBright));
    private Button Button(string title, Action action, bool enabled = true)
    {
        var b = SWConsoleStyle.Action(title, SWConsoleStyle.Accent, !enabled);
        b.HorizontalExpand = true;
        if (!enabled) b.ToolTip = !_state.CanManage ? "Требуется карта министра здравоохранения и активное ведомство" : !_state.MinisterActive ? "Министр не действует" : "Недоступно на текущем этапе или не выполнены условия выбора и снабжения";
        b.OnPressed += _ => action();
        return b;
    }
    private void Send(SWHealthAction action, string world = "", string target = "", int value = 0) => ActionRequested?.Invoke(new(action, world, target, value));
    public void SetState(SWHealthView state)
    {
        _state = state;
        // Keep an open keyboard-filtered destination menu stable during publications.
        if (_pickers.Any(p => p.OptionsScroll.Parent?.Parent != null)) return;
        Rebuild();
    }
    private static bool HasOutbreak(SWHealthWorldView world) => world.Infection > 1f;
    private IEnumerable<SWHealthWorldView> RecipientWorlds() => _state.Worlds
        .OrderByDescending(HasOutbreak)
        .ThenByDescending(w => HasOutbreak(w) ? w.Infection : 0)
        .ThenBy(w => w.Name)
        .ThenBy(w => w.Id);
    private IEnumerable<SWHealthWorldView> ProducerWorlds() => _state.Worlds.Where(w => w.HasProductionSite || w.CanProduce)
        .OrderByDescending(w => w.CanProduce)
        .ThenBy(w => w.HasOutbreakTarget ? w.ClosestOutbreakTravelHours ?? double.PositiveInfinity : double.PositiveInfinity)
        .ThenBy(w => w.Name)
        .ThenBy(w => w.Id);
    private string ProducerLabel(SWHealthWorldView world)
    {
        if (!world.CanProduce) return $"{world.Name}, {world.ProductionStatus}";
        if (!world.HasOutbreakTarget) return world.Name;
        if (world.ClosestOutbreakWorldId == world.Id) return world.Name + ", В очаге";
        return world.ClosestOutbreakTravelHours is { } hours
            ? $"{world.Name}, {hours:0.##} ч до {Name(world.ClosestOutbreakWorldId)}"
            : world.Name + ", Маршрут недоступен";
    }
    private sealed class RecipientOptionButton(int outbreakCount) : OptionButton
    {
        public override void ButtonOverride(Button button)
        {
            if (ItemCount <= outbreakCount) button.Label.FontColorOverride = SWConsoleStyle.Warn;
        }
    }
    private void Picker(string caption, IEnumerable<SWHealthWorldView> worlds, string selected, Action<string> select, bool recipients = false, bool producers = false)
    {
        Text(caption, SWConsoleStyle.Brass);
        var options = worlds.ToArray();
        var picker = recipients ? new RecipientOptionButton(options.Count(HasOutbreak)) : new OptionButton();
        picker.Name = "HealthPicker_" + caption;
        picker.Filterable = true;
        picker.HorizontalExpand = true;
        picker.Disabled = options.Length == 0;
        if (producers) picker.ToolTip = "Время пути до ближайшего очага. Без производства, снабжения и ожидания транспорта.";
        for (var i = 0; i < options.Length; i++)
        {
            var world = options[i];
            picker.AddItem(producers ? ProducerLabel(world) : recipients && HasOutbreak(world) ? $"{world.Name}, заражение {world.Infection:0.#}%" : world.Name, i);
            picker.SetItemMetadata(i, world.Id);
            if (producers && !world.CanProduce) picker.SetItemDisabled(i, true);
        }
        var index = System.Array.FindIndex(options, w => w.Id == selected);
        if (index >= 0) picker.SelectId(index);
        picker.OnItemSelected += args =>
        {
            var world = options[args.Id];
            // An open popup can outlive a capacity publication; do not select its stale option.
            if (producers && !_state.Worlds.Any(w => w.Id == world.Id && w.CanProduce)) { Rebuild(); return; }
            select(world.Id);
            Rebuild();
        };
        _pickers.Add(picker);
        _body.AddChild(picker);
        if (options.Length == 0) Text("Подходящих миров нет", SWConsoleStyle.Warn);
    }
    private void Rebuild()
    {
        _candidateCards = null;
        _pickers.Clear();
        _body.RemoveAllChildren();
        var r = _state.Research;
        var stage = _selected ?? r.Stage;
        if (!_state.Worlds.Any(w => w.Id == _source && w.Infection >= .5f)) _source = _state.Worlds.FirstOrDefault(w => w.Infection >= .5f)?.Id ?? "";
        if (!_state.Worlds.Any(w => w.Id == _lab && w.CanResearch)) _lab = _state.Worlds.FirstOrDefault(w => w.CanResearch)?.Id ?? "";
        if (!_state.Worlds.Any(w => w.Id == _producer && (w.HasProductionSite || w.CanProduce)))
            _producer = ProducerWorlds().FirstOrDefault(w => w.CanProduce)?.Id ?? "";
        if (!_state.Worlds.Any(w => w.Id == _recipient)) _recipient = RecipientWorlds().FirstOrDefault()?.Id ?? "";
        for (var i = 0; i < Titles.Length; i++)
        {
            _stageButtons[i].Text = $"{i + 1}  {Titles[i]}";
            _stageButtons[i].Pressed = i == (int) stage;
            _stageButtons[i].ToolTip = i < (int) r.Stage ? "Этап завершён" : i == (int) r.Stage ? "Текущий этап" : null;
            if (_stageButtons[i] is SWConsoleButton stageButton)
                stageButton.SetAppearance(i < (int) r.Stage ? SWConsoleStyle.Good : i == (int) r.Stage ? SWConsoleStyle.Accent : SWConsoleStyle.TextDim, selected: i == (int) stage);
        }
        Text($"{_state.PathogenName}   штамм {_state.Strain}   бюджет {_state.Budget:N0} кр", SWConsoleStyle.Brass);
        if (r.Stale) Text("Штамм изменился. Эффективность текущей партии снижена.", SWConsoleStyle.Warn);
        Text(r.Status, r.Stale ? SWConsoleStyle.Warn : SWConsoleStyle.Accent);
        _body.AddChild(SWConsoleStyle.Bar(r.Progress, 0, 100, SWConsoleStyle.Accent));
        var eta = r.HoursLeft < 0 ? "Ожидание поставок или возобновления" : $"{r.HoursLeft:0.0} активных ч";
        var fundingRate = r.Stage <= SWHealthStage.Analysis ? $"   {r.FundingPerHour} кр/ч" : "";
        Text($"Этап: {Titles[(int) r.Stage]}   {r.Progress:0}%   {eta}{fundingRate}", SWConsoleStyle.TextDim);
        if (r.Stage <= SWHealthStage.Analysis)
        {
            var funding = Row();
            for (var i = 0; i <= 2; i++)
            {
                var level = i;
                var fundingButton = Button(new[] { "Пауза", "Базовое", "Усиленное" }[i], () => Send(SWHealthAction.SetFunding, value: level), _state.CanManage && r.Funding != i);
                fundingButton.ToggleMode = true; fundingButton.Pressed = r.Funding == i;
                funding.AddChild(fundingButton);
            }
            _body.AddChild(funding);
        }
        var can = _state.CanManage && _state.MinisterActive;
        switch (stage)
        {
            case SWHealthStage.Samples:
                _body.AddChild(new SWHealthSpecimenDiagram(r.Samples, r.Progress));
                Text($"Образцов: {r.Samples}   источник: {Name(r.SourceWorldId)}", SWConsoleStyle.Brass);
                Picker("Очаг для отбора", _state.Worlds.Where(w => w.Infection >= .5f), _source, id => _source = id);
                _body.AddChild(Button(r.Stale && r.Stage >= SWHealthStage.Production ? "Получить образец нового штамма" : "Получить образец", () => Send(SWHealthAction.CollectSample, _source), can && _source != "" && _state.PathogenActive && (r.Stale && r.Stage >= SWHealthStage.Production || r.Stage <= SWHealthStage.Analysis && r.Samples < 2 && (r.Samples == 0 || r.SourceWorldId != _source))));
                if (r.Samples == 1) _body.Children.OfType<Button>().Last().ToolTip = "Сравнить данные двух очагов";
                foreach (var w in _state.Worlds.Where(w => w.Infection > 0))
                { Text($"{w.Name}   заражение {w.Infection:0.0}%"); _body.AddChild(SWConsoleStyle.Bar(w.Infection, 0, 100, SWConsoleStyle.Bad)); }
                break;
            case SWHealthStage.Analysis:
                var names = new[] { "Распространение", "Тяжесть", "Изменчивость" };
                var values = new[] { r.Spread, r.Severity, r.Variability };
                _analysisDiagram ??= new SWHealthAnalysisDiagram(r);
                _analysisDiagram.SetState(r);
                _body.AddChild(_analysisDiagram);
                var analysis = Row();
                for (var i = 0; i < 3; i++)
                {
                    var focus = i;
                    var focusButton = Button(names[i], () => Send(SWHealthAction.FocusAnalysis, value: focus), can && r.Stage == SWHealthStage.Analysis);
                    focusButton.ToggleMode = true; focusButton.Pressed = r.Focus == i;
                    analysis.AddChild(focusButton);
                    Text($"{names[i]}: {(values[i] < 0 ? "не изучено" : values[i].ToString("0.0"))}");
                    if (values[i] >= 0) _body.AddChild(SWConsoleStyle.Bar(values[i], 0, 100, SWConsoleStyle.Accent));
                }
                _body.AddChild(analysis);
                break;
            case SWHealthStage.Candidate:
                var candidates = new BoxContainer { Name = "HealthCandidateComparison", Orientation = Width < 850 ? LayoutOrientation.Vertical : LayoutOrientation.Horizontal, HorizontalExpand = true, SeparationOverride = 14 };
                _candidateCards = candidates;
                foreach (var i in new[] { 0, 1 })
                {
                    var candidate = i;
                    var card = Stack();
                    card.Name = "HealthCandidate" + i;
                    card.AddChild(SWConsoleStyle.Wrap(i == 0 ? "Узкий препарат\nБыстрее и дешевле; действие сильнее зависит от текущего штамма." : "Устойчивый препарат\nБольше времени и ресурсов; лучше сохраняет действие при мутации.", SWConsoleStyle.TextBright));
                    card.AddChild(SWConsoleStyle.Wrap($"Расчётная эффективность {(i == 0 ? r.TargetedEfficacy : r.ResilientEfficacy):0.#}%\nСохранение после мутации {(i == 0 ? r.TargetedMutationRetention : r.ResilientMutationRetention):0.#}%\nИспытания {(i == 0 ? r.TargetedTrialHours : r.ResilientTrialHours):0.#} ч\nПартия {(i == 0 ? r.TargetedBatchCost : r.ResilientBatchCost)} кр", SWConsoleStyle.Brass));
                    card.AddChild(Button(r.Candidate == i ? "Подтвердить выбор" : "Выбрать", () => Send(SWHealthAction.SelectCandidate, value: candidate), can && (r.Stage == SWHealthStage.Candidate || r.Stage == SWHealthStage.Trials && !r.TrialsRunning && !r.Ready)));
                    candidates.AddChild(card);
                }
                _body.AddChild(candidates);
                Text(r.Ready ? "Испытания завершены" : "Эффективность — предварительная оценка до завершения испытаний", SWConsoleStyle.TextDim);
                break;
            case SWHealthStage.Trials:
                if (r.ExtensionComplete) Text("Расширенная проверка завершена. Потеря эффективности при мутации снижена на 25%.", SWConsoleStyle.Good);
                else if (r.Extended && r.Ready) Text("Дополнительная проверка продолжается. Одобренную версию можно выпускать.", SWConsoleStyle.Accent);
                Text($"Лаборатория: {Name(r.TrialsRunning || r.Ready ? r.LaboratoryWorldId : _lab)}   наблюдений: {r.TrialTreated.Count}");
                if (!string.IsNullOrEmpty(r.TrialStatus) && r.TrialStatus != r.Status) Text(r.TrialStatus, r.TrialsRunning ? SWConsoleStyle.Warn : SWConsoleStyle.TextDim);
                for (var index = 0; index < r.TrialMaterials.Count; index++)
                    _body.AddChild(MaterialCard(r.TrialMaterials[index], index, paused: !_state.DayActive));
                Text("Заражение, %", SWConsoleStyle.TextDim);
                _body.AddChild(new SWHealthTrialPlot(r.TrialTreated, r.TrialControl) { Name = "HealthTrialPlot" });
                var legend = Row();
                legend.AddChild(SWConsoleStyle.Text("Препарат", SWConsoleStyle.Accent, SWConsoleStyle.Small));
                legend.AddChild(SWConsoleStyle.Text("Контроль", SWConsoleStyle.Bad, SWConsoleStyle.Small));
                legend.AddChild(new Control { HorizontalExpand = true });
                legend.AddChild(SWConsoleStyle.Text("Наблюдения", SWConsoleStyle.TextDim, SWConsoleStyle.Small));
                _body.AddChild(legend);
                if (!r.TrialsRunning && !r.Ready)
                    Picker("Медицинская база", _state.Worlds.Where(w => w.CanResearch), _lab, id => _lab = id);
                var trialStart = Button(r.TrialsRunning ? "Испытания запущены" : r.Ready ? "Испытания завершены" : $"Начать испытания за {r.TrialCost:N0} кр", () => Send(SWHealthAction.StartTrials, _lab),
                    can && r.Stage == SWHealthStage.Trials && !r.TrialsRunning && !r.Ready && _lab != "" && _state.Budget >= r.TrialCost);
                trialStart.Name = "HealthStartTrials";
                if (!r.TrialsRunning && !r.Ready && _state.Budget < r.TrialCost)
                {
                    var shortage = $"Недостаточно средств: требуется {r.TrialCost:N0} кр, доступно {_state.Budget:N0} кр";
                    trialStart.ToolTip = shortage;
                    Text(shortage, SWConsoleStyle.Warn);
                }
                _body.AddChild(trialStart);
                var extend = Button(r.Extended ? "Расширенная серия выбрана" : r.Ready ? "Расширить проверку за 150 кр" : "Расширить проверку (+150 кр к запуску)",
                    () => Send(SWHealthAction.ExtendTrials), can && !r.Extended && !r.TrialsRunning && (r.Stage == SWHealthStage.Trials || r.Ready) && (!r.Ready || _state.Budget >= 150));
                extend.Name = "HealthExtendTrials";
                if (r.Ready && !r.Extended && _state.Budget < 150) extend.ToolTip = "Недостаточно средств: расширенная проверка стоит 150 кр";
                _body.AddChild(extend);
                break;
            case SWHealthStage.Production:
                Picker("Производитель", ProducerWorlds(), _producer, id => _producer = id, producers: true);
                var producer = _state.Worlds.FirstOrDefault(w => w.Id == _producer);
                if (producer is { HasProductionSite: true })
                {
                    if (!string.IsNullOrEmpty(producer.ProductionStatus)) Text(producer.ProductionStatus, SWConsoleStyle.Warn);
                    if (producer.ProductionPowerRequired > 0)
                        Text($"Свободная энергия: {SWHealthDeliveryText.Quantity(producer.ProductionPowerAvailable)} / {SWHealthDeliveryText.Quantity(producer.ProductionPowerRequired)}   Персонал: {SWHealthDeliveryText.Quantity(producer.ProductionLaborAvailable)} / {SWHealthDeliveryText.Quantity(producer.ProductionLaborRequired)}", SWConsoleStyle.TextDim);
                    if (producer.CanProduce && producer.ProductionRate > 0)
                    {
                        var duration = SWConsoleStyle.Wrap($"Выпуск {_batch} парт.: {SWHealthDeliveryText.Duration(_batch / producer.ProductionRate)}", SWConsoleStyle.Accent);
                        duration.ToolTip = "При текущей мощности, после получения материалов. Без ожидания очереди и доставки. Игровое время; ночью производство приостановлено.";
                        _body.AddChild(duration);
                    }
                }
                else if (producer == null) Text("Нет производств со свободной энергией и персоналом", SWConsoleStyle.Warn);
                Picker("Получатель", RecipientWorlds(), _recipient, id => _recipient = id, recipients: true);
                var recipient = _state.Worlds.FirstOrDefault(w => w.Id == _recipient);
                if (recipient != null) Text($"Охват партии: {Math.Min(100, recipient.BatchCoverage * _batch):0.#}% населения", SWConsoleStyle.TextDim);
                var batches = Row();
                foreach (var n in new[] { 1, 2, 3 })
                {
                    var amount = n;
                    var batchButton = Button(n == 1 ? "1 партия" : $"{n} партии", () => { _batch = amount; Rebuild(); });
                    batchButton.ToggleMode = true; batchButton.Pressed = _batch == n;
                    batches.AddChild(batchButton);
                }
                _body.AddChild(batches);
                _body.AddChild(Button($"Заказать за {_state.BatchCost * _batch:N0} кр", () =>
                {
                    if (_state.Worlds.Any(w => w.Id == _producer && w.CanProduce)) Send(SWHealthAction.OrderBatch, _producer, _recipient, _batch);
                    else Rebuild();
                }, can && r.Ready && _state.Budget >= _state.BatchCost * _batch && producer?.CanProduce == true && _recipient != ""));
                Orders(false);
                break;
            case SWHealthStage.Delivery:
                Orders(true);
                foreach (var w in _state.Worlds) { Text($"{w.Name}   иммунитет {w.Immunity:0.0}%   медицинский охват {w.CareCoverage:0.0}%"); _body.AddChild(SWConsoleStyle.Bar(w.Immunity, 0, 100, SWConsoleStyle.Good)); }
                break;
        }
    }
    internal static Control MaterialCard(SWHealthMaterialView material, int index, string? name = null, bool showStock = true, bool paused = false)
    {
        var card = SWConsoleStyle.Row();
        card.Name = name ?? "HealthTrialMaterial" + index;
        if (material.Priority) card.ToolTip = "Приоритет лабораторных поставок";
        var rows = Stack();
        rows.SeparationOverride = 3;
        rows.AddChild(SWConsoleStyle.Wrap(material.Name, SWConsoleStyle.Brass));
        if (material.Priority) rows.AddChild(SWConsoleStyle.Wrap("Приоритетное снабжение", SWConsoleStyle.Accent));
        var stock = material.StockKnown ? material.RepublicStock.ToString("0.###") : "нет данных";
        rows.AddChild(SWConsoleStyle.Wrap($"Получено: {SWHealthDeliveryText.Quantity(material.Delivered)}/{SWHealthDeliveryText.Quantity(material.Required)}   В пути: {SWHealthDeliveryText.Quantity(material.Incoming)}" + (showStock ? $"   Запас на складах Республики: {stock}" : ""), SWConsoleStyle.TextDim));
        var received = SWHealthDeliveryText.Received(material);
        if (!received)
            rows.AddChild(SWConsoleStyle.Wrap("Не хватает: " + SWHealthDeliveryText.Quantity(Math.Max(0, material.Required - material.Delivered)), SWConsoleStyle.Warn));
        foreach (var timing in SWHealthDeliveryText.Material(material))
        {
            var eta = SWConsoleStyle.Wrap(timing, received ? SWConsoleStyle.Good : SWConsoleStyle.TextBright);
            eta.ToolTip = "Игровое время; ночью доставка приостановлена";
            rows.AddChild(eta);
        }
        if (paused && !received) rows.AddChild(SWConsoleStyle.Wrap("Ночная пауза", SWConsoleStyle.Warn));
        var progress = SWConsoleStyle.Bar((float) material.Delivered, 0, Math.Max(.000001f, (float) material.Required), SWConsoleStyle.Good);
        progress.Name = name == null ? "HealthTrialMaterialProgress" + index : name + "Progress";
        rows.AddChild(progress);
        card.AddChild(rows);
        return card;
    }
    private string Name(string id) => _state.Worlds.FirstOrDefault(w => w.Id == id)?.Name ?? (id == "" ? "не выбран" : id);
    private void Orders(bool delivery)
    {
        if (_state.Orders.Count == 0) Text("Партий в очереди нет", SWConsoleStyle.TextDim);
        foreach (var order in _state.Orders)
        {
            Text($"{order.Name}, {Name(order.ProducerWorldId)} в {Name(order.TargetWorldId)}, штамм {order.Strain}", SWConsoleStyle.Brass);
            Text(order.Status);
            _body.AddChild(SWConsoleStyle.Bar(order.Progress, 0, 100, order.Completed ? SWConsoleStyle.Good : SWConsoleStyle.Accent));
            var payload = SWHealthDeliveryText.ProducedPayload(order);
            if (payload != null)
                _body.AddChild(MaterialCard(payload, 0, "HealthPayload_" + order.Id, showStock: false, paused: !_state.DayActive));
            if (!delivery || payload == null)
                for (var index = 0; index < order.Materials.Count; index++)
                    _body.AddChild(MaterialCard(order.Materials[index], index, "HealthOrderMaterial_" + order.Id + "_" + index, paused: !_state.DayActive));
        }
    }
}

public sealed class SWHealthTrialPlot : Control
{
    private readonly IReadOnlyList<float> _treated, _control;
    public SWHealthTrialPlot(IReadOnlyList<float> treated, IReadOnlyList<float> control)
    { _treated = treated; _control = control; MinHeight = 220; HorizontalExpand = true; }
    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var width = Math.Max(1, Width - 32); var height = Math.Max(1, Height - 24);
        Vector2 Point(int index, float value, int count) => new Vector2(24 + width * index / Math.Max(1, count - 1), 8 + height * (1 - Math.Clamp(value / 100, 0, 1))) * UIScale;
        for (var i = 0; i <= 4; i++) handle.DrawLine(new Vector2(24, 8 + height * i / 4) * UIScale, new Vector2(24 + width, 8 + height * i / 4) * UIScale, SWConsoleStyle.Edge);
        void Series(IReadOnlyList<float> values, Color color)
        {
            for (var i = 0; i < values.Count; i++)
            {
                var p = Point(i, values[i], values.Count);
                if (i > 0) handle.DrawLine(Point(i - 1, values[i - 1], values.Count), p, color);
                handle.DrawCircle(p, 3 * UIScale, color);
            }
        }
        Series(_control, SWConsoleStyle.Bad); Series(_treated, SWConsoleStyle.Accent);
    }
}




/// <summary>Three property lobes reveal the actual measured pathogen properties.</summary>
public sealed class SWHealthAnalysisDiagram : Control
{
    private readonly IConfigurationManager _configuration = IoCManager.Resolve<IConfigurationManager>();
    private SWHealthResearchView _research;
    private float _scanTime = 1;
    private float _progress, _spread, _severity, _variability;
    public SWHealthAnalysisDiagram(SWHealthResearchView research)
    {
        _research = research; MinHeight = 180; HorizontalExpand = true;
        SetState(research);
    }
    public void SetState(SWHealthResearchView research)
    {
        if (research.Stage == SWHealthStage.Analysis && (research.Progress != _progress || research.Spread != _spread
            || research.Severity != _severity || research.Variability != _variability)) _scanTime = 0;
        _progress = research.Progress; _spread = research.Spread; _severity = research.Severity; _variability = research.Variability;
        _research = research;
    }
    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (_configuration.GetCVar(CCVars.ReducedMotion)) { _scanTime = 1; return; }
        for (Control? ancestor = this; ancestor != null; ancestor = ancestor.Parent) if (!ancestor.Visible) return;
        _scanTime = Math.Min(1, _scanTime + args.DeltaSeconds / .65f);
    }
    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var center = new Vector2(Width / 2, Height / 2) * UIScale;
        var radius = Math.Min(Height * .34f, Width * .18f) * UIScale;
        handle.DrawCircle(center, radius * .5f, SWConsoleStyle.Edge);
        if (_scanTime < 1 && !_configuration.GetCVar(CCVars.ReducedMotion))
        {
            var x = Width * (.15f + .7f * _scanTime);
            handle.DrawLine(new Vector2(x, 12) * UIScale, new Vector2(x, Height - 12) * UIScale,
                SWConsoleStyle.Accent.WithAlpha((1 - _scanTime) * .45f));
        }
        var values = new[] { _research.Spread, _research.Severity, _research.Variability };
        for (var i = 0; i < 3; i++)
        {
            var angle = -MathF.PI / 2 + i * MathF.Tau / 3;
            var p = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
            var known = values[i] >= 0;
            var ink = known ? (i == _research.Focus ? SWConsoleStyle.Brass : SWConsoleStyle.Accent) : SWConsoleStyle.Edge;
            handle.DrawLine(center, p, ink);
            handle.DrawCircle(p, 19 * UIScale, SWConsoleStyle.BgRowAlt);
            handle.DrawCircle(p, (known ? 4 + Math.Clamp(values[i], 0, 100) * .12f : 3) * UIScale, ink);
        }
    }
}




/// <summary>Collected specimens and ongoing acquisition, without simulated observations.</summary>
public sealed class SWHealthSpecimenDiagram : Control
{
    private readonly int _samples;
    private readonly float _progress;
    public SWHealthSpecimenDiagram(int samples, float progress) { _samples = samples; _progress = progress; MinHeight = 130; HorizontalExpand = true; }
    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var count = Math.Max(1, _samples);
        var width = Math.Min(Width, 480);
        for (var i = 0; i < count; i++)
        {
            var x = (Width - width) / 2 + width * (i + .5f) / count;
            var center = new Vector2(x, Height / 2) * UIScale;
            handle.DrawCircle(center, 35 * UIScale, SWConsoleStyle.Edge);
            handle.DrawCircle(center, 31 * UIScale, SWConsoleStyle.BgRowAlt);
            if (i >= _samples) continue;
            handle.DrawCircle(center, 12 * UIScale, SWConsoleStyle.Accent);
            handle.DrawCircle(center + new Vector2(-15, 13) * UIScale, 5 * UIScale, SWConsoleStyle.Brass);
            handle.DrawCircle(center + new Vector2(16, -10) * UIScale, 5 * UIScale, SWConsoleStyle.Brass);
        }
        handle.DrawLine(new Vector2(Width * .2f, Height - 8) * UIScale,
            new Vector2(Width * (.2f + .6f * Math.Clamp(_progress / 100, 0, 1)), Height - 8) * UIScale, SWConsoleStyle.Accent);
    }
}









