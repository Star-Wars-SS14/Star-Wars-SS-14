using System;
using System.Linq;
using Content.Shared._SW._Senate;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._SW._Senate;

/// <summary>Review a solution and its cost before committing it. Budget ticks do not replace the editor.</summary>
public sealed class SWMinistryRequestView : BoxContainer
{
    public event Action<int, int>? ResolveRequested;
    public event Action<int>? AuditRequested;
    public event Action<int, string>? RefuseRequested;
    public event Action<int, string>? ReasonChanged;
    public LineEdit ReasonEdit { get; }
    public Button ConfirmButton { get; }
    public int? SelectedOption { get; private set; }
    public int RequestId => _request.Id;
    private SWMinistryRequestEntry _request;
    private int _budget;
    private readonly Label _budgetLabel;
    private readonly Label _adminLabel;
    private readonly Label _balanceLabel;
    private readonly RichTextLabel _deadlineLabel;
    private readonly RichTextLabel _auditLabel;
    private readonly Button _auditButton;
    private readonly BoxContainer _preview;
    private readonly BoxContainer _effects;
    private readonly Button[] _options;

    public SWMinistryRequestView(SWMinistryRequestEntry request, int budget, int auditCost, string reason, int admin = 0)
    {
        _request = request;
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        SeparationOverride = 12;
        var body = SWConsoleDocument.Body(request.ProblemName, request.ProblemDescription);
        var metadata = SWConsoleDocument.Metadata(("Отправитель", request.OwnerLabel));
        _deadlineLabel = SWConsoleStyle.Wrap(string.Empty, SWConsoleStyle.TextDim);
        _budgetLabel = SWConsoleStyle.Text(string.Empty, SWConsoleStyle.TextBright, SWConsoleStyle.Strong);
        _adminLabel = SWConsoleStyle.Text(string.Empty, SWConsoleStyle.TextDim, SWConsoleStyle.Small);
        _auditLabel = SWConsoleStyle.Wrap(string.Empty, SWConsoleStyle.TextDim);
        metadata.AddChild(_deadlineLabel);
        metadata.AddChild(SWConsoleStyle.Section("Ресурсы"));
        metadata.AddChild(_budgetLabel);
        metadata.AddChild(_adminLabel);
        metadata.AddChild(_auditLabel);
        _auditButton = SWConsoleStyle.Action("Провести аудит", SWConsoleStyle.Accent);
        _auditButton.OnPressed += _ => AuditRequested?.Invoke(_request.Id);
        metadata.AddChild(_auditButton);
        if (request.Audited && !string.IsNullOrWhiteSpace(request.IssueDescription))
            body.AddChild(SWConsoleStyle.Wrap(request.IssueDescription, SWConsoleStyle.Warn));
        if (request.SideEffects.Count > 0)
        {
            body.AddChild(SWConsoleStyle.Section("Побочные последствия"));
            foreach (var effect in request.SideEffects)
                body.AddChild(SWConsoleStyle.Wrap($"{effect.StatName}   {effect.Delta:+0;-0}", effect.Color));
        }
        body.AddChild(SWConsoleStyle.Section("Решение"));
        _options = new Button[request.Options.Count];
        for (var i = 0; i < request.Options.Count; i++)
        {
            var index = i;
            var option = request.Options[i];
            var button = SWConsoleStyle.Action($"{option.Label} ({option.Cost} кр.)", SWConsoleStyle.TextBright);
            button.HorizontalExpand = true;
            button.ClipText = true;
            button.ToolTip = option.Label;
            button.OnPressed += _ => SelectOption(index);
            _options[i] = button;
            body.AddChild(button);
        }
        _preview = new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, SeparationOverride = 6 };
        body.AddChild(_preview);
        _effects = new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, SeparationOverride = 6 };
        metadata.AddChild(_effects);
        _balanceLabel = SWConsoleStyle.Text(string.Empty, SWConsoleStyle.TextDim, SWConsoleStyle.Small);
        body.AddChild(_balanceLabel);
        ConfirmButton = SWConsoleStyle.Primary("Подтвердить", disabled: true);
        ConfirmButton.OnPressed += _ =>
        {
            if (SelectedOption is { } index && _request.Options[index].Affordable)
                ResolveRequested?.Invoke(_request.Id, index);
        };
        body.AddChild(ConfirmButton);
        var refusal = new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true,
            SeparationOverride = 8, Visible = !string.IsNullOrWhiteSpace(reason) };
        var expandRefusal = SWConsoleStyle.Action("Указать причину отказа…", SWConsoleStyle.TextDim);
        expandRefusal.OnPressed += _ => refusal.Visible = !refusal.Visible;
        body.AddChild(expandRefusal);
        ReasonEdit = new LineEdit { PlaceHolder = "Укажите причину…", Text = reason, HorizontalExpand = true };
        ReasonEdit.OnTextChanged += args => ReasonChanged?.Invoke(_request.Id, args.Text);
        refusal.AddChild(ReasonEdit);
        var refuse = SWConsoleStyle.Action("Отказать в обращении", SWConsoleStyle.Bad);
        refuse.OnPressed += _ => RefuseRequested?.Invoke(_request.Id, ReasonEdit.Text);
        refusal.AddChild(refuse);
        body.AddChild(refusal);
        AddChild(new SWConsoleColumns(body, metadata));
        UpdateRequest(request, budget, auditCost, admin);
    }

    public void UpdateRequest(SWMinistryRequestEntry request, int budget, int auditCost, int admin = 0)
    {
        _request = request;
        _budget = budget;
        _budgetLabel.Text = $"Бюджет: {budget} кр.";
        _adminLabel.Text = $"Админ-ресурс: {admin}";
        _deadlineLabel.SetMessage(request.DeadlineLabel, defaultColor: SWConsoleStyle.TextDim);
        _auditLabel.SetMessage($"Аудит: {request.AuditLabel}", defaultColor: SWConsoleStyle.TextDim);
        _auditButton.Visible = request.CanAudit;
        _auditButton.Text = $"Аудит ({auditCost} админ.)";
        UpdateDecision();
    }

    public void SelectOption(int index)
    {
        if (index < 0 || index >= _request.Options.Count)
            return;
        SelectedOption = index;
        for (var i = 0; i < _options.Length; i++)
            SWConsoleStyle.StyleTabButton(_options[i], i == index);
        _preview.RemoveAllChildren();
        var option = _request.Options[index];
        _preview.AddChild(SWConsoleStyle.Wrap(option.Description, SWConsoleStyle.TextBright));
        _effects.RemoveAllChildren();
        SWConsoleDocument.Effects(_effects, option.Effects);
        UpdateDecision();
    }

    private void UpdateDecision()
    {
        if (SelectedOption is not { } index)
        {
            ConfirmButton.Disabled = true;
            return;
        }
        var option = _request.Options[index];
        ConfirmButton.Disabled = !option.Affordable;
        ConfirmButton.Text = option.Affordable ? $"Подтвердить ({option.Cost} кр.)" : "Недостаточно ресурсов";
        _balanceLabel.Text = $"После решения: {_budget - option.Cost} кр.";
        _balanceLabel.FontColorOverride = option.Affordable ? SWConsoleStyle.TextDim : SWConsoleStyle.Bad;
    }

    public static string Revision(SWMinistryRequestEntry request) =>
        $"{request.Id}|{request.ProblemName}|{request.ProblemDescription}|{request.OwnerLabel}|{request.Audited}|{request.AuditInProgress}|{request.IssueDescription}|" +
        string.Join(";", request.Options.Select(o => $"{o.Label}:{o.Description}:{o.Cost}:" +
            string.Join(",", o.Effects.Select(e => $"{e.StatName}:{e.Delta}:{e.Color}")))) + "|" +
        string.Join(";", request.SideEffects.Select(e => $"{e.StatName}:{e.Delta}:{e.Color}"));
}
