using Content.Shared._SW.SaberForms;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Shared.Prototypes;

namespace Content.Client._SW.SaberForms;

[UsedImplicitly]
public sealed class SWSaberFormsBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private SWSaberFormsWindow? _window;
    private bool _centerOnFirstState;

    protected override void Open()
    {
        base.Open();
        _centerOnFirstState = !UiSystem.TryGetPosition(Owner, UiKey, out _);
        _window = this.CreateWindow<SWSaberFormsWindow>();
        _window.FormSelected += SelectForm;
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is not SWSaberFormsUiState forms || _window == null)
            return;

        _window.SetForms(forms);
        if (_centerOnFirstState && _window.Parent is { } parent && parent.Width > 0 && parent.Height > 0)
        {
            _centerOnFirstState = false;
            _window.Measure(parent.Size);
            _window.RecenterWindow(new(0.5f, 0.5f));
        }
    }

    private void SelectForm(ProtoId<SWSaberFormPrototype> form, NetEntity weapon)
    {
        SendMessage(new SWSaberFormSelectMessage(form, weapon));
    }
}
