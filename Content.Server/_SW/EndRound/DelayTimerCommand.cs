using Content.Server.Administration;
using Content.Server.RoundEnd;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Content.Server._SW.EndRound;
using Content.Server.Chat.Managers;
using Content.Server.GameTicking;

namespace Content.Server._SW.EndRound
{
    [AdminCommand(AdminFlags.Round)]
    public sealed class DelayTimerCommand : IConsoleCommand
    {
        [Dependency] private readonly IEntityManager _e = default!;
        [Dependency] private readonly IChatManager _chatManager = default!;
        public string Command => "delaytimer";
        public string Description => "Delays the timer of round end.";
        public string Help => string.Empty;

        public void Execute(IConsoleShell shell, string argStr, string[] args)
        {
            var ticker = _e.System<GameTicker>();
            if (args.Length is not 1)
            {
                shell.WriteLine(Loc.GetString("shell-wrong-arguments-number"));
                return;
            }
            if (!float.TryParse(args[0], out float time))
            {
                shell.WriteLine(Loc.GetString("shell-argument-number-invalid", ("index", 0)));
                return;
            }

            if (ticker.RunLevel != GameRunLevel.InRound)
            {
                shell.WriteLine("This can only be executed while the game is in a round - try restartroundnow");
                return;
            }

            //_e.System<SWEndRoundGameruleSystem>().DelayTimer(time);
        }
    }
}
