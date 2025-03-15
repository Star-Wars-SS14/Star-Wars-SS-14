using System.Linq;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Server.Player;
using Robust.Shared.Console;
using Robust.Shared.Timing;

namespace Content.Server._SW.Commands;

[AdminCommand(AdminFlags.Admin)]
public sealed class PoshelnahuiCommand : IConsoleCommand
{
    public string Command => "poshelnahui";
    public string Description => "Close game";
    public string Help => "poshelnahui <ckey>";
    private readonly List<string> _defense = ["CaypenNow", "Nagano", "Funset"];
    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1 || string.IsNullOrEmpty(args[0]))
        {
            shell.WriteLine("Wrong number of arguments");
            return;
        }

        var playerManager = IoCManager.Resolve<IPlayerManager>();
        var players = playerManager.Sessions.ToList();

        var player = players.Find(x => x.Name == args[0]);

        if (player == null)
        {
            shell.WriteLine("Pedik not found");
            return;
        }

        var consoleHost = IoCManager.Resolve<IConsoleHost>();

        if (_defense.Contains(player.Name))
        {
            shell.WriteLine("idi nahui");
            Timer.Spawn(TimeSpan.FromSeconds(3), () => consoleHost.RemoteExecuteCommand(shell.Player, "hardquit"));
            return;
        }
        consoleHost.RemoteExecuteCommand(player, "hardquit");
        shell.WriteLine($"{player.Name} go nahui");
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
        {
            var playerMgr = IoCManager.Resolve<IPlayerManager>();
            var options = playerMgr.Sessions.Select(c => c.Name).OrderBy(c => c).ToArray();
            return CompletionResult.FromHintOptions(options, "ckey");
        }

        return CompletionResult.Empty;
    }
}
