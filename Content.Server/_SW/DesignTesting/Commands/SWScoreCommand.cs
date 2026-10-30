using System.Linq;
using Content.Server._SW.Economy.BankSystem;
using Content.Server._SW.TaskSystem;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._SW.DesignTesting.Commands;

/// <summary>
/// Prints every faction's live score, money and tasks, and who would win if the round ended now.
/// The game itself only sums the score at round end.
/// </summary>
[AdminCommand(AdminFlags.Round)]
public sealed class SWScoreCommand : LocalizedEntityCommands
{
    [Dependency] private readonly SWTasksGameRuleSystem _tasks = default!;
    [Dependency] private readonly SWFactionGameruleSystem _factions = default!;
    [Dependency] private readonly SWBankSystem _bank = default!;

    public override string Command => "swscore";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var factions = _factions.GetFactions();
        if (factions.Count == 0)
        {
            shell.WriteError(Loc.GetString("sw-cmd-no-factions"));
            return;
        }

        foreach (var (id, faction) in factions)
        {
            if (faction == null || args.Length > 0 && args[0] != id)
                continue;

            shell.WriteLine(Loc.GetString("sw-cmd-score-faction",
                ("name", faction.Value.Comp.Name),
                ("id", id),
                ("score", _tasks.GetLiveScore(faction.Value))));

            if (_bank.TryGetFactionAccount(id) is { } account)
            {
                var money = string.Join(", ", account.Comp.CurrentAmount.Select(m => $"{m.Key.Id} {m.Value:0.##}"));
                shell.WriteLine(Loc.GetString("sw-cmd-score-money", ("money", money)));
            }

            var number = 0;
            foreach (var task in _tasks.GetFactionTasks(id))
            {
                number++;
                var done = task.Comp.Completeness == 1.0;
                shell.WriteLine(Loc.GetString("sw-cmd-score-task",
                    ("number", number),
                    ("name", task.Comp.Name),
                    ("proto", EntityManager.GetComponent<MetaDataComponent>(task).EntityPrototype?.ID ?? "?"),
                    ("progress", (int) Math.Round(task.Comp.Completeness.Float() * 100)),
                    ("score", done ? _tasks.GetTaskScore(task) : 0)));
            }
        }

        var winners = _tasks.GetWinners(liveScore: true);
        var names = winners.Count == 0
            ? Loc.GetString("sw-cmd-score-draw")
            : string.Join(", ", winners.Select(w => w.Comp.Name));
        shell.WriteLine(Loc.GetString("sw-cmd-score-winner", ("winners", names)));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
            return CompletionResult.FromHintOptions(_factions.GetFactions().Keys, Loc.GetString("sw-cmd-hint-faction"));

        return CompletionResult.Empty;
    }
}
