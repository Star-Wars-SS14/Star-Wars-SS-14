using System.Linq;
using System.Threading.Tasks;
using Content.Server._SW.Cosmetics;
using Content.Server.Administration;
using Content.Server.Administration.Logs;
using Content.Server.Database;
using Content.Shared._SW.BattlePass;
using Content.Shared.Administration;
using Content.Shared.Database;
using Robust.Server.Player;
using Robust.Shared.Console;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server._SW.BattlePass;

/// <summary>
/// Admin control of a player's season. There is no payment code anywhere, and nothing here opens the paid track.
/// Works on offline players too. Every change goes to the admin log.
/// </summary>
[AdminCommand(AdminFlags.Host)]
public sealed class SWBattlePassCommand : LocalizedEntityCommands
{
    [Dependency] private readonly IAdminLogManager _adminLog = default!;
    [Dependency] private readonly IPlayerLocator _locator = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IServerDbManager _db = default!;
    [Dependency] private readonly SWBattlePassSystem _pass = default!;
    [Dependency] private readonly SWCosmeticSystem _cosmetics = default!;

    public override string Command => "swbp";

    public override async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 2)
        {
            shell.WriteLine(Help);
            return;
        }

        var located = await _locator.LookupIdByNameOrIdAsync(args[1]);
        if (located == null)
        {
            shell.WriteError(Loc.GetString("sw-cmd-battlepass-no-player", ("name", args[1])));
            return;
        }

        var user = located.UserId;
        if (_pass.CurrentSeason is not { } season)
        {
            shell.WriteError(Loc.GetString("sw-cmd-battlepass-no-season"));
            return;
        }

        switch (args[0])
        {
            case "xp" when args.Length == 3 && int.TryParse(args[2], out var amount):
                await Change(shell, user, season, $"gave {amount} XP", data =>
                {
                    data.Xp = Math.Max(0, data.Xp + amount);
                    data.State.XpBySource["admin"] = data.State.XpBySource.GetValueOrDefault("admin") + amount;
                });
                break;

            case "tier" when args.Length == 3 && int.TryParse(args[2], out var tier) && tier >= 0:
                await Change(shell, user, season, $"set tier {tier}", data =>
                {
                    data.Xp = SWBattlePassRules.XpForTier(season, Math.Min(tier, season.Tiers.Count));
                });
                break;

            case "case" when args.Length == 4
                             && int.TryParse(args[3], out var count)
                             && _proto.HasIndex<SWBattlePassCasePrototype>(args[2]):
                var caseId = args[2];
                await Change(shell, user, season, $"gave {count} of {caseId}", data =>
                {
                    data.State.Cases[caseId] = Math.Max(0, data.State.Cases.GetValueOrDefault(caseId) + count);
                });
                break;

            case "reset" when args.Length == 2:
                await Reset(shell, user, located.Username, season);
                break;

            case "list" when args.Length == 2:
                await List(shell, user, season);
                break;

            default:
                shell.WriteLine(Help);
                break;
        }
    }

    private async Task Change(
        IConsoleShell shell,
        NetUserId user,
        SWBattlePassSeasonPrototype season,
        string what,
        Action<SWBattlePassData> action)
    {
        await _pass.ModifyAsync(user, action);
        await _pass.FlushAsync();

        if (_player.TryGetSessionById(user, out var session))
            _pass.SendState(session);

        _adminLog.Add(LogType.Action,
            LogImpact.Medium,
            $"Battle pass: {shell.Player?.Name ?? "console"} {what} for {user} in {season.ID}.");

        await List(shell, user, season);
    }

    /// <summary>
    /// Clears a player's season and takes back what it gave. The paid track stays as it was. Items of the sale are never on
    /// the season's lists, so they stay too.
    /// </summary>
    private async Task Reset(IConsoleShell shell, NetUserId user, string userName, SWBattlePassSeasonPrototype season)
    {
        var fromSeason = new HashSet<string>();
        foreach (var tier in season.Tiers)
        {
            foreach (var reward in new[] { tier.Free, tier.Paid })
            {
                if (reward == null)
                    continue;

                if (reward.Cosmetic is { } cosmetic)
                    fromSeason.Add(cosmetic);

                fromSeason.UnionWith(reward.Also.Select(also => also.Id));
            }
        }

        foreach (var box in _proto.EnumeratePrototypes<SWBattlePassCasePrototype>())
        {
            foreach (var entry in box.Entries)
            {
                if (entry.Cosmetic is { } cosmetic)
                    fromSeason.Add(cosmetic);
            }
        }

        var owned = (await _cosmetics.GetOwned(user)).Where(fromSeason.Contains).ToList();
        foreach (var cosmetic in owned)
        {
            await _cosmetics.SetOwned(user, userName, cosmetic, false, shell.Player?.Name ?? "console");
        }

        await Change(shell, user, season, $"reset the season, took back {owned.Count} cosmetics", data =>
        {
            data.Xp = 0;
            data.State = new SWBattlePassSavedState();
            data.Granted.Clear();
        });

        _pass.DropInvalidCosmetics(user);
    }

    private async Task List(IConsoleShell shell, NetUserId user, SWBattlePassSeasonPrototype season)
    {
        SWBattlePassProgress? row = null;
        var data = _pass.GetData(user);
        if (data == null)
            row = await _db.GetSWBattlePassProgress(user.UserId, season.ID);

        var xp = data?.Xp ?? row?.Xp ?? 0;
        var paid = data?.Paid ?? row?.Paid ?? false;
        var state = data?.State ?? (row != null ? SWBattlePassSavedState.Deserialize(row.State) : new SWBattlePassSavedState());
        var owned = await _cosmetics.GetOwned(user);

        shell.WriteLine(Loc.GetString("sw-cmd-battlepass-list",
            ("user", user.ToString()),
            ("season", season.ID),
            ("xp", xp),
            ("tier", SWBattlePassRules.TierOf(season, xp)),
            ("paid", paid.ToString()),
            ("snowflakes", state.Snowflakes),
            ("bank", state.CatchUpBank),
            ("pity", state.CasesSincePity)));
        shell.WriteLine($"claimed: {string.Join(", ", state.Claimed)}");
        shell.WriteLine($"cases: {string.Join(", ", state.Cases.Select(c => $"{c.Key} x{c.Value}"))}");
        shell.WriteLine($"owned: {string.Join(", ", owned)}");
        shell.WriteLine($"xp by source: {string.Join(", ", state.XpBySource.Select(c => $"{c.Key} {c.Value}"))}");
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHintOptions(new[] { "xp", "tier", "case", "reset", "list" }, Loc.GetString("sw-cmd-battlepass-hint-action")),
            2 => CompletionResult.FromHintOptions(CompletionHelper.SessionNames(), Loc.GetString("sw-cmd-battlepass-hint-player")),
            _ => CompletionResult.Empty,
        };
    }
}
