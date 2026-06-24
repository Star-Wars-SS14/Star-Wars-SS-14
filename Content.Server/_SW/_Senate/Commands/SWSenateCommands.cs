using System.Globalization;
using System.Linq;
using Content.Server.Administration;
using Content.Shared.Administration;
using Content.Shared._SW._Senate.Dilemmas;
using Content.Shared._SW._Senate.Sectors;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Content.Server._SW._Senate.Core;
using Content.Server._SW._Senate.Economy;

namespace Content.Server._SW._Senate.Commands;

// Дебаг-команды режима Сената. Все под админ-флагом Debug.
// Системы достаются через IoC, так как консольные команды не являются EntitySystem.

// swtime — перевести игровое время на конкретный час дня (07–21).
// Полезно догнать время до 15:00/20:00, чтобы проверить применение/провал дилемм,
// или до окна заседания. Минута необязательна.
// Примеры: swtime 14   |   swtime 19 45
[AdminCommand(AdminFlags.Debug)]
public sealed class SWTimeCommand : IConsoleCommand
{
    public string Command => "swtime";
    public string Description => "Перевести время Сената на указанный час.";
    public string Help => "swtime <час 7-21> [минута]";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 1 || !int.TryParse(args[0], out var hour))
        {
            shell.WriteError(Help);
            return;
        }

        var minute = 0;
        if (args.Length >= 2 && !int.TryParse(args[1], out minute))
        {
            shell.WriteError(Help);
            return;
        }

        var time = IoCManager.Resolve<IEntityManager>().System<SWSenateTimeSystem>();
        time.SetTime(hour, minute);
        shell.WriteLine(time.TimeString());
    }
}

// swdilemma — выдать дилемму вне расписания.
// Первый аргумент: ID сектора (секторальная дилемма этому сектору) либо "republic" (канцлерская).
// Второй аргумент опционален: конкретный ID дилеммы; без него берётся случайная из пула нужного типа.
// Тип проверяется (секторальная/республиканская), дедлайн и применение — конец дня (21:00).
// Примеры:
//   swdilemma SWSectorAlderaan                       — случайная секторальная Альдераану
//   swdilemma SWSectorAlderaan SWDilemmaSecPlague    — конкретная секторальная
//   swdilemma republic                               — случайная канцлерская
//   swdilemma republic SWDilemmaRepBorder            — конкретная канцлерская
[AdminCommand(AdminFlags.Debug)]
public sealed class SWDilemmaCommand : IConsoleCommand
{
    public string Command => "swdilemma";
    public string Description => "Назначить дилемму сектору (или республике) — случайную или по ID.";
    public string Help => "swdilemma <sectorId|republic> [dilemmaId]";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 1)
        {
            shell.WriteError(Help);
            return;
        }

        var entMan = IoCManager.Resolve<IEntityManager>();
        var proto = IoCManager.Resolve<IPrototypeManager>();
        var dilemmas = entMan.System<SWSenateDilemmaSystem>();

        var target = args[0];
        SWDilemmaScope scope;
        string owner;
        string? sectorId;

        if (string.Equals(target, "republic", StringComparison.OrdinalIgnoreCase))
        {
            scope = SWDilemmaScope.Republic;
            owner = SWSenateDilemmaSystem.RepublicOwner;
            sectorId = null;
        }
        else
        {
            if (!proto.HasIndex<SWSectorPrototype>(target))
            {
                shell.WriteError($"Сектор не найден: {target}");
                return;
            }

            scope = SWDilemmaScope.Sectoral;
            owner = target;
            sectorId = target;
        }

        string dilemmaId;
        if (args.Length >= 2)
        {
            if (!proto.TryIndex<SWDilemmaPrototype>(args[1], out var dProto))
            {
                shell.WriteError($"Дилемма не найдена: {args[1]}");
                return;
            }

            if (dProto.Scope != scope)
            {
                shell.WriteError($"Дилемма {args[1]} не подходит по типу ({dProto.Scope}).");
                return;
            }

            dilemmaId = args[1];
        }
        else
        {
            var pool = proto.EnumeratePrototypes<SWDilemmaPrototype>()
                .Where(d => d.Scope == scope && string.IsNullOrEmpty(d.Trigger))
                .ToList();

            if (pool.Count == 0)
            {
                shell.WriteError("Нет подходящих дилемм в пуле.");
                return;
            }

            dilemmaId = IoCManager.Resolve<IRobustRandom>().Pick(pool).ID;
        }

        dilemmas.AdminAddDilemma(scope, owner, sectorId, dilemmaId);
        shell.WriteLine($"Дилемма {dilemmaId} назначена: {target}");
    }
}

// swstat — напрямую задать статы (выставить значение, не прибавить).
//   policy <subId> <value>             — установить саб-стат политики Республики (0–100).
//   sector <sectorId> <globalId> <value> — установить глобал-стат конкретного сектора (его «факт»).
//   fill <value>                       — установить ВСЕ сабы политики и ВСЕ глобалы всех секторов разом.
// Замечание: значения секторов со временем дрейфуют к политике+смещению, так что для устойчивого
// эффекта меняй и политику (policy/fill). Значение — дробное, точка как разделитель.
// Примеры:
//   swstat policy SWStatTreasury 90
//   swstat sector SWSectorKuat SWGlobalIndustry 100
//   swstat fill 100
[AdminCommand(AdminFlags.Debug)]
public sealed class SWStatCommand : IConsoleCommand
{
    public string Command => "swstat";
    public string Description => "Задать статы Сената: политику (саб), сектор (глобал) или всё разом.";
    public string Help => "swstat policy <subId> <value> | swstat sector <sectorId> <globalId> <value> | swstat fill <value>";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 2)
        {
            shell.WriteError(Help);
            return;
        }

        var entMan = IoCManager.Resolve<IEntityManager>();
        var proto = IoCManager.Resolve<IPrototypeManager>();
        var senate = entMan.System<SWSenateStateSystem>();

        switch (args[0].ToLowerInvariant())
        {
            case "policy":
                if (args.Length < 3 || !TryParseValue(args[2], out var policyValue))
                {
                    shell.WriteError("swstat policy <subId> <value>");
                    return;
                }

                if (!proto.HasIndex<SWStatPrototype>(args[1]))
                {
                    shell.WriteError($"Саб не найден: {args[1]}");
                    return;
                }

                senate.SetPolicy(args[1], policyValue);
                shell.WriteLine($"{args[1]} = {policyValue}");
                break;

            case "sector":
                if (args.Length < 4 || !TryParseValue(args[3], out var sectorValue))
                {
                    shell.WriteError("swstat sector <sectorId> <globalId> <value>");
                    return;
                }

                if (!senate.SetSectorGlobal(args[1], args[2], sectorValue))
                {
                    shell.WriteError($"Не найден сектор/глобал: {args[1]} {args[2]}");
                    return;
                }

                shell.WriteLine($"{args[1]} {args[2]} = {sectorValue}");
                break;

            case "fill":
                if (!TryParseValue(args[1], out var fillValue))
                {
                    shell.WriteError("swstat fill <value>");
                    return;
                }

                senate.FillAll(fillValue);
                shell.WriteLine($"Все статы = {fillValue}");
                break;

            default:
                shell.WriteError(Help);
                break;
        }
    }

    private static bool TryParseValue(string raw, out float value) =>
        float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
