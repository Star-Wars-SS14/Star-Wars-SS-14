using Content.Server.Administration;
using Content.Shared._SW.Tutorial;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._SW.Tutorial;

/// <summary>
/// Fallback way into and out of the tutorial, for when the lobby button is not reachable.
/// </summary>
[AnyCommand]
public sealed class SWTutorialCommand : LocalizedEntityCommands
{
    [Dependency] private readonly SWTutorialSystem _tutorial = default!;

    public override string Command => "swtutorial";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player)
        {
            shell.WriteError(Loc.GetString("shell-only-players-can-run-this-command"));
            return;
        }

        var argument = args.Length > 0 ? args[0] : "new";
        SWTutorialChoice mode;
        switch (argument)
        {
            case "exit":
                if (!_tutorial.TryExit(player))
                    shell.WriteError(Loc.GetString("sw-tutorial-error-not-started"));
                return;
            case "new":
                mode = SWTutorialChoice.NewToGame;
                break;
            case "server":
                mode = SWTutorialChoice.NewToServer;
                break;
            default:
                shell.WriteError(Loc.GetString("cmd-swtutorial-help"));
                return;
        }

        if (!_tutorial.TryStart(player, mode, out var error) && error != null)
            shell.WriteError(Loc.GetString(error.Value));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length == 1
            ? CompletionResult.FromOptions(["new", "server", "exit"])
            : CompletionResult.Empty;
    }
}
