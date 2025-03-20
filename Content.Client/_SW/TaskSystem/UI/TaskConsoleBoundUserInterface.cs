using Content.Client.Lathe.UI;
using Content.Shared._SW;
using Content.Shared.Lathe;
using Content.Shared.Research.Components;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
namespace Content.Client._SW.TaskSystem.UI;

public sealed partial class TaskConsoleBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private TaskConsoleMenu? _menu;
    public TaskConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
       
    }
    protected override void Open()
    {
        base.Open();
        _menu = this.CreateWindow<TaskConsoleMenu>();
        //_menu.SetEntity(Owner);
        _menu.OpenCenteredRight();
        _menu.PopulateTasks();
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
            case TaskConsoleUpdateState msg:
                
                //if (_menu != null)
                //    _menu.Recipes = msg.Recipes;
                //_menu?.PopulateRecipes();
                //_menu?.UpdateCategories();
                //_menu?.PopulateQueueList(msg.Queue);
                //_menu?.SetQueueInfo(msg.CurrentlyProducing);
                break;
        }
    }
}

