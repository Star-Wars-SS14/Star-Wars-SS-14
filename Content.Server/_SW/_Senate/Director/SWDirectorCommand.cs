using Content.Server.Administration;
using Content.Server.EUI;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._SW._Senate.Director;

[AdminCommand(AdminFlags.Admin)]
public sealed class SWDirectorCommand : IConsoleCommand
{
    public string Command => "swdirector";
    public string Description => "Открыть панель режиссёра (Империя).";
    public string Help => "swdirector";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player)
        {
            shell.WriteError("Команда доступна только из клиента.");
            return;
        }

        var eui = IoCManager.Resolve<EuiManager>();
        eui.OpenEui(new SWDirectorEui(), player);
    }
}
