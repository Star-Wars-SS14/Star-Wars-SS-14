using System.Globalization;
using System.Numerics;
using Content.Server._SW.DesignTesting.Bots;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Map;

namespace Content.Server._SW.DesignTesting.Commands;

/// <summary>
/// Spawns soldier bots of a faction where the admin stands, or at given map coordinates.
/// </summary>
[AdminCommand(AdminFlags.Spawn)]
public sealed class SWBotsCommand : LocalizedEntityCommands
{
    [Dependency] private readonly SWCombatBotSystem _bots = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private const int MaxCount = 100;

    public override string Command => "swbots";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length is not (2 or 3 or 5 or 6))
        {
            shell.WriteError(Loc.GetString("shell-wrong-arguments-number"));
            shell.WriteLine(Help);
            return;
        }

        if (_bots.GetBotPrototype(args[0]) == null)
        {
            shell.WriteError(Loc.GetString("sw-cmd-bots-no-bot", ("faction", args[0])));
            return;
        }

        if (!int.TryParse(args[1], out var count) || count < 1 || count > MaxCount)
        {
            shell.WriteError(Loc.GetString("sw-cmd-bots-bad-count", ("max", MaxCount)));
            return;
        }

        var objective = SWBotObjective.EnemyBase;
        if (args.Length > 2 && !Enum.TryParse(args[2], true, out objective))
        {
            shell.WriteError(Loc.GetString("sw-cmd-bots-bad-objective", ("objective", args[2])));
            return;
        }

        MapCoordinates at;
        if (args.Length >= 5)
        {
            if (!float.TryParse(args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                || !float.TryParse(args[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
            {
                shell.WriteError(Loc.GetString("shell-argument-number-invalid", ("index", 4)));
                return;
            }

            var mapId = shell.Player?.AttachedEntity is { } player
                ? _transform.GetMapId(player)
                : MapId.Nullspace;
            if (args.Length == 6 && int.TryParse(args[5], out var mapArg))
                mapId = new MapId(mapArg);

            at = new MapCoordinates(new Vector2(x, y), mapId);
        }
        else if (shell.Player?.AttachedEntity is { } player)
        {
            at = _transform.GetMapCoordinates(player);
        }
        else
        {
            shell.WriteError(Loc.GetString("sw-cmd-bots-no-position"));
            return;
        }

        if (at.MapId == MapId.Nullspace)
        {
            shell.WriteError(Loc.GetString("sw-cmd-bots-no-position"));
            return;
        }

        // "here" sends the bots to where they spawn, so they guard it.
        var bots = _bots.SpawnBots(args[0], count, at, objective, objective == SWBotObjective.Point ? at : null);
        shell.WriteLine(Loc.GetString("sw-cmd-bots-spawned",
            ("count", bots.Count),
            ("faction", args[0]),
            ("objective", objective.ToString())));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHintOptions(new[] { "RepublicFaction", "EmpireFaction" },
                Loc.GetString("sw-cmd-hint-faction")),
            2 => CompletionResult.FromHint(Loc.GetString("sw-cmd-bots-hint-count")),
            3 => CompletionResult.FromHintOptions(Enum.GetNames<SWBotObjective>(),
                Loc.GetString("sw-cmd-bots-hint-objective")),
            4 => CompletionResult.FromHint("[x]"),
            5 => CompletionResult.FromHint("[y]"),
            6 => CompletionResult.FromHint("[mapId]"),
            _ => CompletionResult.Empty,
        };
    }
}
