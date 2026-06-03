using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._SW.Cutscenes;

[AdminCommand(AdminFlags.Fun)]
public sealed class SWPlayCutsceneCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entityManager = default!;

    public string Command => "playcutscene";
    public string Description => "Plays a WebM cutscene for all connected clients.";
    public string Help => "Usage: playcutscene <path>\nExample: playcutscene /Video/_SW/Cutscenes/intro.webm";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteLine(Help);
            return;
        }

        var path = NormalizePath(args[0]);
        _entityManager.System<SWCutsceneSystem>().PlayCutscene(path);
        shell.WriteLine($"Playing cutscene for all clients: {path}");
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length == 1
            ? CompletionResult.FromHint("<path>")
            : CompletionResult.Empty;
    }

    private static string NormalizePath(string path)
    {
        return path.StartsWith('/') ? path : $"/{path}";
    }
}
