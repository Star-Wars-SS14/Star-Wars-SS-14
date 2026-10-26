using System;
using System.Linq;
using System.Text;
using Content.Shared._SW._Senate;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client._SW._Senate.Consoles.Work;

/// <summary>Sector projects with a persistent destination selector and live delivery progress.</summary>
public sealed class SWProgramPanel : BoxContainer
{
    private readonly RichTextLabel _budget = SWConsoleStyle.Wrap(string.Empty, SWConsoleStyle.TextDim);
    private readonly OptionButton _worlds = new()
    {
        Name = "ProgramWorldPicker", Filterable = true, HorizontalExpand = true,
    };
    private readonly Label _worldLabel;
    private readonly BoxContainer _programs = new()
    {
        Name = "Programs", Orientation = LayoutOrientation.Vertical, HorizontalExpand = true,
        SeparationOverride = 16,
    };
    private readonly List<SWProgramWorldEntry> _worldEntries = new();
    private readonly List<(Button Button, SWProgramEntry Program)> _startButtons = new();
    private SWSenateWorkConsoleState? _state;
    private string? _sectorId;
    private string _worldSignature = string.Empty;
    private string? _programSignature;

    public string? SelectedWorldId { get; private set; }
    public event Action<string, string>? StartRequested;

    public SWProgramPanel()
    {
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        VerticalExpand = true;
        SeparationOverride = 6;
        AddChild(_budget);
        var destination = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 12 };
        destination.AddChild(SWConsoleStyle.Text("Мир", SWConsoleStyle.TextDim, SWConsoleStyle.Body));
        _worldLabel = _worlds.Children.OfType<BoxContainer>().Single().Children.OfType<Label>().Single();
        _worldLabel.FontOverride = SWConsoleStyle.Body;
        _worldLabel.ClipText = true;
        _worlds.OnItemSelected += args => SelectWorld(_worldEntries[args.Id].Id);
        destination.AddChild(_worlds);
        AddChild(destination);
        var scroll = new ScrollContainer
        {
            Name = "ProgramsScroll", HorizontalExpand = true, VerticalExpand = true, HScrollEnabled = false,
            Margin = new Thickness(0, 6, 0, 0),
        };
        scroll.AddChild(_programs);
        AddChild(scroll);
    }

    public void SetState(SWSenateWorkConsoleState state)
    {
        _state = state;
        _budget.SetMessage(state.BudgetLocked
            ? $"Бюджет сектора: {state.MyBudgetAvailable} кр."
            : $"Бюджет сектора: {state.MyBudgetAvailable} кр.   Ожидает утверждения");

        if (_sectorId != state.SectorId)
        {
            _sectorId = state.SectorId;
            SelectedWorldId = null;
            _worldSignature = string.Empty;
        }

        var worldSignature = string.Join("|", state.ProgramWorlds.Select(w => $"{w.Id.Length}:{w.Id}{w.Name.Length}:{w.Name}"));
        if (_worldSignature != worldSignature || _worldEntries.Count == 0)
        {
            _worldSignature = worldSignature;
            _worldEntries.Clear();
            _worldEntries.AddRange(state.ProgramWorlds);
            _worlds.Clear();
            for (var i = 0; i < _worldEntries.Count; i++)
                _worlds.AddItem(_worldEntries[i].Name, i);
            if (!SelectWorld(SelectedWorldId))
                SelectWorld(_worldEntries.FirstOrDefault()?.Id);
        }
        _worlds.Disabled = _worldEntries.Count == 0;
        if (_worlds.Disabled)
        {
            SelectedWorldId = null;
            _worldLabel.Text = "Нет доступных миров";
        }

        var signature = Signature(state.Programs);
        if (signature != _programSignature)
        {
            _programSignature = signature;
            _programs.RemoveAllChildren();
            _startButtons.Clear();
            if (state.Programs.Count == 0)
                _programs.AddChild(SWConsoleStyle.Wrap("Программ нет.", SWConsoleStyle.TextDim));
            foreach (var program in state.Programs)
                _programs.AddChild(BuildProgram(program));
        }
        RefreshStartButtons();
    }

    public bool SelectWorld(string? id)
    {
        var index = _worldEntries.FindIndex(w => w.Id == id);
        if (index < 0)
            return false;
        SelectedWorldId = id;
        _worlds.SelectId(index);
        _worlds.ToolTip = _worldEntries[index].Name;
        RefreshStartButtons();
        return true;
    }

    private void RefreshStartButtons()
    {
        foreach (var (button, program) in _startButtons)
            button.Disabled = program.Started || program.Applied || !program.Affordable ||
                              _state is not { BudgetLocked: true } || SelectedWorldId == null;
    }

    private Control BuildProgram(SWProgramEntry program)
    {
        var section = new BoxContainer
        {
            Name = $"Program_{program.Id}", Orientation = LayoutOrientation.Vertical,
            HorizontalExpand = true, SeparationOverride = 4,
        };
        if (!string.IsNullOrWhiteSpace(program.Category))
            section.AddChild(SWConsoleStyle.Wrap(program.Category, SWConsoleStyle.TextDim));
        var title = SWConsoleStyle.Wrap(program.Name, SWConsoleStyle.TextBright);
        title.Stylesheet = new Stylesheet(new StyleRule[]
        {
            StylesheetHelpers.Element<RichTextLabel>().Prop("font", SWConsoleStyle.Header),
        });
        section.AddChild(title);
        if (!string.IsNullOrWhiteSpace(program.Description))
            section.AddChild(SWConsoleStyle.Wrap(program.Description, SWConsoleStyle.TextDim));

        if (program.Started || program.Applied)
        {
            if (!string.IsNullOrWhiteSpace(program.WorldName))
                section.AddChild(SWConsoleStyle.Wrap(program.WorldName, SWConsoleStyle.Brass));
            section.AddChild(SWConsoleStyle.Wrap(program.Status,
                program.Applied ? SWConsoleStyle.Good : SWConsoleStyle.Accent));
        }

        if (program.Materials.Count > 0)
        {
            section.AddChild(SWConsoleStyle.Section(program.Started ? "Снабжение" : "Материалы"));
            foreach (var material in program.Materials)
            {
                var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 12 };
                row.AddChild(SWConsoleStyle.Wrap(material.Name, SWConsoleStyle.TextBright));
                var quantities = new BoxContainer { Orientation = LayoutOrientation.Vertical };
                quantities.AddChild(SWConsoleStyle.Text(program.Started
                        ? $"{material.Delivered:0.##} / {material.Required:0.##} ед."
                        : $"{material.Required:0.##} ед.",
                    material.Delivered >= material.Required ? SWConsoleStyle.Good : SWConsoleStyle.TextDim,
                    SWConsoleStyle.Body));
                if (program.Started && material.InTransit > 0)
                    quantities.AddChild(SWConsoleStyle.Text($"В пути: {material.InTransit:0.##}",
                        SWConsoleStyle.Accent, SWConsoleStyle.Small));
                row.AddChild(quantities);
                section.AddChild(row);
            }
            if (program.Started)
            {
                // Goods have different units; every required material contributes an equal share.
                var received = program.Materials.Average(m => m.Required > 0
                    ? Math.Clamp(m.Delivered / m.Required, 0, 1)
                    : 1);
                var supply = SWConsoleStyle.Bar(received, 0, 1, SWConsoleStyle.Accent);
                supply.Name = "ProgramSupplyProgress";
                section.AddChild(supply);
            }
        }

        section.AddChild(SWConsoleStyle.Wrap(program.Started
                ? $"{(program.Materials.Count > 0 ? "Работы" : "Исполнение")}: {program.WorkedHours:0.##} / {program.WorkHours:0.##} ч"
                : $"Срок работ: {program.WorkHours:0.##} ч",
            SWConsoleStyle.TextDim));
        if (program.Started && program.WorkHours > 0)
        {
            var work = SWConsoleStyle.Bar(Math.Clamp(program.WorkedHours, 0, program.WorkHours), 0,
                program.WorkHours, program.Applied ? SWConsoleStyle.Good : SWConsoleStyle.Brass);
            work.Name = "ProgramWorkProgress";
            section.AddChild(work);
        }

        if (program.Effects.Count > 0)
        {
            section.AddChild(SWConsoleStyle.Section(program.Applied ? "Результат" : "После завершения"));
            var effects = new WrapContainer { HorizontalExpand = true };
            foreach (var effect in program.Effects)
            {
                var label = SWConsoleStyle.Wrap($"{effect.StatName} {effect.Delta:+0.##;-0.##;0}", effect.Color, 260);
                label.HorizontalExpand = false;
                label.MaxWidth = 260;
                label.Margin = new Thickness(0, 0, 12, 0);
                effects.AddChild(label);
            }
            section.AddChild(effects);
        }

        if (!program.Started && !program.Applied)
        {
            var start = SWConsoleStyle.Action($"Финансировать — {program.Cost} кр.", SWConsoleStyle.Accent);
            start.Name = $"StartProgram_{program.Id}";
            start.HorizontalAlignment = HAlignment.Left;
            start.Margin = new Thickness(0, 6, 0, 0);
            start.OnPressed += _ =>
            {
                if (!start.Disabled && SelectedWorldId is { } world)
                    StartRequested?.Invoke(program.Id, world);
            };
            _startButtons.Add((start, program));
            section.AddChild(start);
        }
        section.AddChild(new PanelContainer
        {
            MinHeight = 1, Margin = new Thickness(0, 12, 0, 0),
            PanelOverride = new Robust.Client.Graphics.StyleBoxFlat { BackgroundColor = SWConsoleStyle.Edge },
        });
        return section;
    }

    private static string Signature(List<SWProgramEntry> programs)
    {
        var signature = new StringBuilder();
        foreach (var program in programs)
        {
            Add(program.Id);
            Add(program.Name);
            Add(program.Category);
            Add(program.Description);
            Add(program.WorldId);
            Add(program.WorldName);
            Add(program.Status);
            signature.Append($"/{program.Cost}/{program.Affordable}/{program.Started}/{program.Applied}/{program.WorkHours}/{program.WorkedHours};");
            foreach (var material in program.Materials)
            {
                Add(material.GoodId);
                Add(material.Name);
                signature.Append($"/{material.Required}/{material.Delivered}/{material.InTransit};");
            }
            foreach (var effect in program.Effects)
            {
                Add(effect.StatName);
                signature.Append($"/{effect.Delta}/{effect.Color};");
            }
        }
        return signature.ToString();

        void Add(string value) => signature.Append(value.Length).Append(':').Append(value);
    }
}
