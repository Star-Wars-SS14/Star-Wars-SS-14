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

        _menu.OpenCenteredRight();

        //_menu.OnServerListButtonPressed += _ =>
        //{
        //    SendMessage(new ConsoleServerSelectionMessage());
        //};

        //_menu.RecipeQueueAction += (recipe, amount) =>
        //{
        //    SendMessage(new LatheQueueRecipeMessage(recipe, amount));
        //};
    }



    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        switch (state)
        {
            case SWTurretConsoleBounderUserInterfaceState msg:
                break;
        }
    }
}

