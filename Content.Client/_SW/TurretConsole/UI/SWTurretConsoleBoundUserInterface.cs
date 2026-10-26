using Content.Client.Lathe.UI;
using Content.Shared._SW;
using Content.Shared.Lathe;
using Content.Shared.Research.Components;
using Robust.Client.UserInterface;

namespace Content.Client._SW.TurretConsole.UI;

public sealed partial class SWTurretConsoleBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private SWTurretConsoleMenu? _menu;
    public SWTurretConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {

    }
    protected override void Open()
    {
        base.Open();
        _menu = this.CreateWindow<SWTurretConsoleMenu>();
        _menu.OnJobButtonPressed += SendToggleMessage;
        _menu.OnTargetHumanoidsWithoutIdToggled += SendToggleTargetHumanoidsWithoutIdMessage;
        _menu.OnTargetAnimalsToggled += SendToggleTargetAnimalsMessage;
        _menu.OpenCenteredRight();
        _menu.OnFactionAllowBtnPressed += SendAllowMessage;
        _menu.OnFactionDisallowBtnPressed += SendDisallowMessage;
    }
    public void SendToggleMessage(string job)
    {
        SendMessage(new SWTurretJobToggleMessage(job));
    }
    public void SendAllowMessage(string f)
    {
        SendMessage(new SWAllowFaction(f));
    }
    public void SendDisallowMessage(string f)
    {
        SendMessage(new SWDisallowFaction(f));
    }
    public void SendToggleTargetHumanoidsWithoutIdMessage()
    {
        SendMessage(new SWToggleTargetHumanoidsWithoutIdMessage());
    }
    public void SendToggleTargetAnimalsMessage()
    {
        SendMessage(new SWToggleTargetAnimalsMessage());
    }
    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        switch (state)
        {
            case SWTurretConsoleBounderUserInterfaceState msg:
                _menu?.UpdateJobs(msg.JobsAllowed);
                _menu?.UpdateFactions(msg.AvailableFactions);
                _menu?.UpdateTargetOptions(msg.TargetHumanoidsWithoutId, msg.TargetAnimals);
                break;
        }
    }
}

