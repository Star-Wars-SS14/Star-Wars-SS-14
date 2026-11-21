using System.Linq;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Server.Player;
using Robust.Shared.Console;
using Robust.Shared.Player;

namespace Content.Server._SW.Cultist;

/// <summary>Explicit manual setup for exercising the ordinary cultist rule.</summary>
[AdminCommand(AdminFlags.Fun)]
public sealed class SWCultistTestCommand : LocalizedEntityCommands
{
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly SWCultistGameRuleSystem _cultists = default!;

    public override string Command => "swcultisttest";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length is not (3 or 4) || args[0] is not ("kill" or "item"))
        {
            shell.WriteError(Loc.GetString("sw-cultist-test-usage"));
            return;
        }
        if (!NetEntity.TryParse(args[1], out var netTarget) || !EntityManager.TryGetEntity(netTarget, out var target))
        {
            shell.WriteError(Loc.GetString("sw-cultist-test-invalid-target"));
            return;
        }
        var players = new List<ICommonSession>();
        foreach (var name in args.Skip(2))
        {
            if (!_players.TryGetSessionByUsername(name, out var player))
            {
                shell.WriteError(Loc.GetString("sw-cultist-test-no-player", ("player", name)));
                return;
            }
            players.Add(player);
        }
        if (!_cultists.TryStartManual(players, target.Value, args[0] == "item", out var error))
        {
            shell.WriteError(error);
            return;
        }
        shell.WriteLine(Loc.GetString("sw-cultist-test-success", ("count", players.Count)));
    }
}
