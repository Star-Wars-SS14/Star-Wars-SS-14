using System.Linq;
using Content.Server._SW.TaskSystem;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;

namespace Content.Server._SW.DesignTesting.Commands;

/// <summary>
/// Gives a faction a task or completes one, so task rewards and scoring can be tested without waiting.
/// </summary>
[AdminCommand(AdminFlags.Round)]
public sealed class SWTaskCommand : LocalizedEntityCommands
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SWTasksGameRuleSystem _tasks = default!;
    [Dependency] private readonly SWFactionGameruleSystem _factions = default!;

    public override string Command => "swtask";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 3)
        {
            shell.WriteError(Loc.GetString("shell-wrong-arguments-number"));
            shell.WriteLine(Help);
            return;
        }

        var faction = args[1];
        if (_factions.GetFactionEntity(faction) == null)
        {
            shell.WriteError(Loc.GetString("sw-cmd-no-faction", ("faction", faction)));
            return;
        }

        switch (args[0])
        {
            case "add":
                Add(shell, faction, args[2], args.Length > 3 && args[3] == "force");
                break;
            case "complete":
                Complete(shell, faction, args[2]);
                break;
            default:
                shell.WriteError(Loc.GetString("sw-cmd-task-unknown-action", ("action", args[0])));
                break;
        }
    }

    private void Add(IConsoleShell shell, string faction, string taskProto, bool force)
    {
        if (!_proto.TryIndex<EntityPrototype>(taskProto, out var proto) || !proto.Components.ContainsKey("SWTask"))
        {
            shell.WriteError(Loc.GetString("sw-cmd-task-no-task", ("task", taskProto)));
            return;
        }

        var task = force ? _tasks.AddTask(faction, taskProto) : _tasks.TryAddTask(faction, taskProto);
        if (task == null)
        {
            shell.WriteError(Loc.GetString("sw-cmd-task-requirements", ("task", taskProto)));
            return;
        }

        shell.WriteLine(Loc.GetString("sw-cmd-task-added",
            ("task", task.Value.Comp.Name),
            ("faction", faction),
            ("number", _tasks.GetFactionTasks(faction).Count)));
    }

    private void Complete(IConsoleShell shell, string faction, string numberArg)
    {
        var tasks = _tasks.GetFactionTasks(faction);
        if (!int.TryParse(numberArg, out var number) || number < 1 || number > tasks.Count)
        {
            shell.WriteError(Loc.GetString("sw-cmd-task-bad-number", ("count", tasks.Count)));
            return;
        }

        var task = tasks[number - 1];
        _tasks.ForceCompleteTask(task);
        shell.WriteLine(Loc.GetString("sw-cmd-task-completed",
            ("task", task.Comp.Name),
            ("score", _tasks.GetTaskScore(task))));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        switch (args.Length)
        {
            case 1:
                return CompletionResult.FromHintOptions(new[] { "add", "complete" },
                    Loc.GetString("sw-cmd-task-hint-action"));
            case 2:
                return CompletionResult.FromHintOptions(_factions.GetFactions().Keys,
                    Loc.GetString("sw-cmd-hint-faction"));
            case 3 when args[0] == "add":
                var taskIds = _proto.EnumeratePrototypes<EntityPrototype>()
                    .Where(p => !p.Abstract && p.Components.ContainsKey("SWTask"))
                    .Select(p => p.ID)
                    .OrderBy(id => id);
                return CompletionResult.FromHintOptions(taskIds, Loc.GetString("sw-cmd-task-hint-task"));
            case 3:
                return CompletionResult.FromHint(Loc.GetString("sw-cmd-task-hint-number"));
            case 4 when args[0] == "add":
                return CompletionResult.FromHintOptions(new[] { "force" }, Loc.GetString("sw-cmd-task-hint-force"));
            default:
                return CompletionResult.Empty;
        }
    }
}
