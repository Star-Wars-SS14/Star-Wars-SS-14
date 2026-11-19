using Content.Server._SW.QuestBoard;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;

namespace Content.Server._SW.QuestBoard.Commands;

/// <summary>
///     Moderator tool: removes a quest board entry by its id.
/// </summary>
[AdminCommand(AdminFlags.Moderator)]
public sealed class SWQuestBoardRemoveCommand : IConsoleCommand
{
    public string Command => "swquestremove";
    public string Description => "Removes a quest from all bulletin boards by id.";
    public string Help => "swquestremove <questId>";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 1 || !int.TryParse(args[0], out var id))
        {
            shell.WriteLine("Usage: swquestremove <questId>");
            return;
        }

        var sys = IoCManager.Resolve<IEntityManager>().System<SWQuestBoardSystem>();
        if (sys.RemoveQuest(id))
            shell.WriteLine($"Quest {id} removed from all boards.");
        else
            shell.WriteLine($"Quest {id} not found.");
    }
}