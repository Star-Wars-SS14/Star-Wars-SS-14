using Content.Shared._SW;
using Content.Shared._SW.Vehicle;
using Robust.Client.UserInterface;

namespace Content.Client._SW.Vehicle.UI;
public sealed partial class SWVehicleBounduserInterface : BoundUserInterface
{
    [ViewVariables]
    private SWVehicleMenu? _menu;
    public SWVehicleBounduserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {

    }
    protected override void Open()
    {
        base.Open();
        _menu = this.CreateWindow<SWVehicleMenu>();
        _menu.OnTake += SendTakeMessage;
    }

    private void SendTakeMessage(string role)
    {

    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        switch (state)
        {
            case SWVehicleMenuBUIState msg:
                _menu?.UpdateRoles(msg.RolesInfo);
                break;
        }
    }
}
