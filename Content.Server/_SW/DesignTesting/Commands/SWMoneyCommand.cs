using System.Globalization;
using Content.Server._SW.Economy.BankSystem;
using Content.Server._SW.TaskSystem;
using Content.Server.Administration;
using Content.Shared._SW.Economy.Prototypes;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;

namespace Content.Server._SW.DesignTesting.Commands;

/// <summary>
/// Adds money or supply points to a faction account, so shops and the top tier can be tested at once.
/// A negative amount takes money away.
/// </summary>
[AdminCommand(AdminFlags.Round)]
public sealed class SWMoneyCommand : LocalizedEntityCommands
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SWFactionGameruleSystem _factions = default!;
    [Dependency] private readonly SWBankSystem _bank = default!;

    public override string Command => "swmoney";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 3)
        {
            shell.WriteError(Loc.GetString("shell-wrong-arguments-number"));
            shell.WriteLine(Help);
            return;
        }

        if (_bank.TryGetFactionAccount(args[0]) is not { } account)
        {
            shell.WriteError(Loc.GetString("sw-cmd-no-faction", ("faction", args[0])));
            return;
        }

        if (!_proto.HasIndex<SWCurrencyPrototype>(args[1]))
        {
            shell.WriteError(Loc.GetString("sw-cmd-money-no-currency", ("currency", args[1])));
            return;
        }

        if (!float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var amount))
        {
            shell.WriteError(Loc.GetString("shell-argument-number-invalid", ("index", 3)));
            return;
        }

        var add = new Dictionary<ProtoId<SWCurrencyPrototype>, float> { [args[1]] = amount };
        account.Comp.CurrentAmount = _bank.Sum(account.Comp.CurrentAmount, add);
        _bank.UpdateAllInterfaces();

        shell.WriteLine(Loc.GetString("sw-cmd-money-done",
            ("faction", args[0]),
            ("currency", args[1]),
            ("amount", account.Comp.CurrentAmount[args[1]])));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHintOptions(_factions.GetFactions().Keys, Loc.GetString("sw-cmd-hint-faction")),
            2 => CompletionResult.FromHintOptions(CompletionHelper.PrototypeIDs<SWCurrencyPrototype>(),
                Loc.GetString("sw-cmd-money-hint-currency")),
            3 => CompletionResult.FromHint(Loc.GetString("sw-cmd-money-hint-amount")),
            _ => CompletionResult.Empty,
        };
    }
}
