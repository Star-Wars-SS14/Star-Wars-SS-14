using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._SW._Senate.Delegations;

[AdminCommand(AdminFlags.Admin)]
public sealed class SWDelegationCommand : IConsoleCommand
{
    public string Command => "swdelegation";
    public string Description => "Состояние представительства или запуск допустимого повода.";
    public string Help => "swdelegation status | swdelegation start <tax|health> <sector-id> <commodity-or-world-id>";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var system = IoCManager.Resolve<IEntityManager>().System<SWDelegationSystem>();
        if (args.Length == 1 && args[0] == "status")
        {
            shell.WriteLine(system.AdminStatus());
            return;
        }
        if (args.Length == 4 && args[0] == "start")
        {
            shell.WriteLine(system.TryAdminStart(args[1], args[2], args[3])
                ? "Представитель ожидает игрока."
                : "Запуск отклонён: повод, держатель, срок или место недоступны.");
            return;
        }
        shell.WriteLine(Help);
    }
}
