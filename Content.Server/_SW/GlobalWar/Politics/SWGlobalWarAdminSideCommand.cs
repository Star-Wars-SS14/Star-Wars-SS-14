using Content.Server.Administration;
using Content.Shared._SW.GlobalWar;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._SW.GlobalWar.Politics;

/// <summary>
/// Admin debugging: picks the side an aghost or a body outside the campaign acts for, so an admin can also capture
/// or command on a world their side does not own. "auto" returns to the side that owns the location.
/// </summary>
[AdminCommand(AdminFlags.Admin)]
public sealed class SWGlobalWarAdminSideCommand : LocalizedEntityCommands
{
    [Dependency] private readonly SWGlobalWarAssignmentsSystem _assignments = default!;

    public override string Command => "gwadminside";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player)
        {
            shell.WriteError(Loc.GetString("shell-only-players-can-run-this-command"));
            return;
        }
        if (args.Length != 1)
        {
            shell.WriteError(Loc.GetString("shell-wrong-arguments-number"));
            shell.WriteLine(Help);
            return;
        }

        SWGlobalWarFaction? side = args[0].ToLowerInvariant() switch
        {
            "republic" => SWGlobalWarFaction.Republic,
            "empire" => SWGlobalWarFaction.Empire,
            "auto" => null,
            _ => SWGlobalWarFaction.Neutral,
        };
        if (side == SWGlobalWarFaction.Neutral)
        {
            shell.WriteError(Loc.GetString("cmd-gwadminside-invalid", ("side", args[0])));
            return;
        }

        _assignments.SetAdminSide(player.UserId, side);
        shell.WriteLine(Loc.GetString(side == null ? "cmd-gwadminside-auto" : "cmd-gwadminside-set",
            ("side", side?.ToString() ?? string.Empty)));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length == 1
            ? CompletionResult.FromOptions(["republic", "empire", "auto"])
            : CompletionResult.Empty;
    }
}
