using Content.Shared._SW.Lathe;
using Robust.Client.UserInterface;

namespace Content.Client._SW.Lathe.SWLathe.UI;

public sealed class SWLatheBoundUserInterface : BoundUserInterface
{
    private SWLatheMenu? _menu;

    public SWLatheBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _menu = this.CreateWindow<SWLatheMenu>();
        _menu.OnRecipeSelected += (recipe, productionDisabled) =>
            SendMessage(new SWLatheSelectRecipeMessage(recipe, productionDisabled));
        _menu.OpenCenteredRight();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is SWLatheBoundUserInterfaceState latheState)
            _menu?.Populate(latheState);
    }
}
