using Content.Shared._SW;
using Content.Shared._SW.Vehicle;
using Robust.Client.UserInterface;
using Robust.Shared.Prototypes;

namespace Content.Client._SW.Vehicle.UI;
public sealed partial class SWVehicleBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private SWVehicleMenu? _menu;
    public SWVehicleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
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
        SendMessage(new SWOccupyRoleMessage(role));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        switch (state)
        {
            case SWVehicleMenuBoundUserInterfaceState msg:
                _menu?.UpdateRoles(msg.RolesInfo);
                break;
        }
    }
}
