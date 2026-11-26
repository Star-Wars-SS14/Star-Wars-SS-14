using System.Globalization;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._SW._Senate.Economy;

[AdminCommand(AdminFlags.Admin)]
public sealed class SWRepublicEconomyCommand : IConsoleCommand
{
    public string Command => "sweconomy";
    public string Description => "Состояние расчёта производства и коэффициенты спроса Республики.";
    public string Help => "sweconomy\nsweconomy demand <world> <group> <multiplier>\nКоэффициент 1 возвращает базовый спрос. Заказы на оборудование не масштабируются.";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var system = IoCManager.Resolve<IEntityManager>().System<SWRepublicEconomySystem>();
        if (args.Length != 0)
        {
            if (args.Length != 4 || args[0] != "demand"
                || !double.TryParse(args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var multiplier)
                || !system.SetDemandMultiplier(args[1], args[2], multiplier))
            {
                shell.WriteError(Help);
                return;
            }
            shell.WriteLine($"Спрос {args[1]}/{args[2]}: коэффициент {multiplier:0.###}, со следующего шага экономики.");
            return;
        }
        if (system.Runner is not { } runner)
        {
            shell.WriteLine("Расчёт экономики не инициализирован.");
            return;
        }
        if (runner.Fault != null)
        {
            shell.WriteError($"Расчёт остановлен: {runner.Fault.Message}");
            return;
        }
        if (system.Snapshot is not { } snapshot)
        {
            shell.WriteLine("Расчёт ожидает начала дня Сената.");
            return;
        }
        shell.WriteLine($"Экономика: {snapshot.Hour:0.##} активных ч; шаг {SWRepublicEconomyRunner.StepHours:0.##} ч; очередь {runner.PendingHours:0.##} ч.");
        shell.WriteLine($"Расчёт: {runner.LastWorkerMilliseconds:0.###} мс, пик {runner.PeakWorkerMilliseconds:0.###} мс; основной поток, пик {runner.PeakMainThreadMilliseconds:0.###} мс.");
        shell.WriteLine($"Фоновая задача: {(runner.Busy ? "выполняется" : "ожидает")}. Данные расчёта пока не меняют бюджет и показатели Республики.");
    }
}
