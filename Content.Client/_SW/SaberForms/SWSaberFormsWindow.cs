using System.Numerics;
using Content.Client.Stylesheets;
using Content.Shared._SW.SaberForms;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Prototypes;

namespace Content.Client._SW.SaberForms;

public sealed class SWSaberFormsWindow : DefaultWindow
{
    private const float PreferredWidth = 350;
    private readonly IPrototypeManager _prototypes = IoCManager.Resolve<IPrototypeManager>();

    private readonly BoxContainer _forms = new()
    {
        Orientation = BoxContainer.LayoutOrientation.Vertical,
        SeparationOverride = 6,
    };

    public event Action<ProtoId<SWSaberFormPrototype>, NetEntity>? FormSelected;

    public SWSaberFormsWindow()
    {
        Title = Loc.GetString("sw-saber-forms-action");
        Resizable = false;
        AllowOffScreen = DirectionFlag.None;
        Contents.AddChild(new ScrollContainer
        {
            HScrollEnabled = false,
            ReserveScrollbarSpace = true,
            ReturnMeasure = true,
            Children = { _forms },
        });
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        availableSize.X = Math.Min(availableSize.X, PreferredWidth);
        var measured = base.MeasureOverride(availableSize);
        return new Vector2(availableSize.X, measured.Y);
    }

    public void SetForms(SWSaberFormsUiState state)
    {
        _forms.RemoveAllChildren();
        foreach (var form in state.AllowedForms)
        {
            if (!_prototypes.TryIndex(form, out var prototype))
                continue;

            var active = form == state.ActiveForm;
            var name = Loc.GetString(prototype.Name);
            var title = new Label
            {
                Text = active ? Loc.GetString("sw-saber-forms-active", ("form", name)) : name,
                StyleClasses = { StyleClass.LabelHeading },
                MouseFilter = MouseFilterMode.Ignore,
            };
            var description = new RichTextLabel { MouseFilter = MouseFilterMode.Ignore };
            description.SetMessage(Loc.GetString(prototype.Description));

            var row = new ContainerButton
            {
                StyleClasses = { ContainerButton.StyleClassButton },
                Children =
                {
                    new BoxContainer
                    {
                        Orientation = BoxContainer.LayoutOrientation.Vertical,
                        SeparationOverride = 2,
                        Margin = new Thickness(4, 6),
                        MouseFilter = MouseFilterMode.Ignore,
                        Children = { title, description },
                    },
                },
            };

            if (active)
            {
                row.AddStyleClass(StyleClass.Positive);
                row.MouseFilter = MouseFilterMode.Ignore;
            }
            else
                row.OnPressed += _ => FormSelected?.Invoke(form, state.Weapon);

            _forms.AddChild(row);
        }
    }
}
