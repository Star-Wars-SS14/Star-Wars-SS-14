using Content.Client.Lathe.UI;
using Content.Shared._SW;
using Content.Shared.Lathe;
using Content.Shared.Research.Components;
using Robust.Client.UserInterface;
namespace Content.Client._SW.TaskSystem.UI;

public sealed partial class TaskConsoleBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private TaskConsoleMenu? _menu;
    public TaskConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
       
    }
     private void RequestUpdateTasks()
    {
        SendMessage(new UpdateTasksMessage());
    }
    protected override void Open()
    {
        base.Open();
        _menu = this.CreateWindow<TaskConsoleMenu>();
        _menu.UpdateTasksEvent += _ => { RequestUpdateTasks(); };

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
            case TaskConsoleBounderUserInterfaceState msg:
                _menu?.PopulateTasks(msg.Tasklist);
                break;
        }
    }
}

