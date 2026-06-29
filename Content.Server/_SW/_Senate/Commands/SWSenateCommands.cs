using System.Globalization;
using System.Linq;
using Content.Server.Administration;
using Content.Shared.Administration;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Crises;
using Content.Shared._SW._Senate.Dilemmas;
using Content.Shared._SW._Senate.Ministries;
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
                .Where(d => d.Scope == scope && d.Conditions.Count == 0)
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

// swambition — предпросмотр итогов по амбициям: для каждого занятого сектора и канцлера показывает
// сколько амбиций выполнено и их текущий статус. Раунд НЕ завершает (официальный подсчёт идёт на экране
// итогов раунда). Удобно для теста, не дожидаясь конца раунда.
[AdminCommand(AdminFlags.Debug)]
public sealed class SWAmbitionCommand : IConsoleCommand
{
    public string Command => "swambition";
    public string Description => "Показать текущее выполнение амбиций сенаторами и канцлером.";
    public string Help => "swambition";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var entMan = IoCManager.Resolve<IEntityManager>();
        var proto = IoCManager.Resolve<IPrototypeManager>();
        var senate = entMan.System<SWSenateStateSystem>();
        var ambition = entMan.System<SWSenateAmbitionSystem>();

        var rows = new List<(string Label, int Score, int Total, List<SWAmbitionEntry> Entries)>();

        foreach (var (sectorId, holder) in senate.SectorHolders)
        {
            var entries = ambition.BuildAmbitions(sectorId);
            if (entries.Count == 0)
                continue;

            var name = proto.TryIndex<SWSectorPrototype>(sectorId, out var p) ? p.Name : sectorId;
            rows.Add(($"{name} — {holder}", entries.Count(e => e.Met), entries.Count, entries));
        }

        if (senate.Chancellor != null)
        {
            var entries = ambition.BuildAmbitions(SWSenateDilemmaSystem.RepublicOwner);
            if (entries.Count > 0)
                rows.Add(($"Канцлер ({senate.Chancellor})", entries.Count(e => e.Met), entries.Count, entries));
        }

        if (rows.Count == 0)
        {
            shell.WriteLine("Активных амбиций нет (нет занятых секторов и канцлера).");
            return;
        }

        rows.Sort((a, b) => b.Score.CompareTo(a.Score));
        foreach (var row in rows)
        {
            shell.WriteLine($"{row.Label}: {row.Score}/{row.Total}");
            foreach (var entry in row.Entries)
                shell.WriteLine($"   [{(entry.Met ? "V" : " ")}] {entry.Name} ({entry.Progress})");
        }
    }
}

// swborder — подвигать давление Империи на границе (для теста механик Обороны).
//   swborder <0-100>        — задать абсолютное значение давления.
//   swborder add <delta>    — прибавить/убавить (delta может быть отрицательным).
// Примеры:
//   swborder 80
//   swborder add 15
//   swborder add -20
[AdminCommand(AdminFlags.Debug)]
public sealed class SWBorderCommand : IConsoleCommand
{
    public string Command => "swborder";
    public string Description => "Задать или сдвинуть давление Империи на границе.";
    public string Help => "swborder <0-100> | swborder add <delta>";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 1)
        {
            shell.WriteError(Help);
            return;
        }

        var defense = IoCManager.Resolve<IEntityManager>().System<SWMinistryDefenseSystem>();

        if (string.Equals(args[0], "add", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length < 2 || !float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var delta))
            {
                shell.WriteError("swborder add <delta>");
                return;
            }

            defense.AddBorder(delta);
        }
        else
        {
            if (!float.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                shell.WriteError(Help);
                return;
            }

            defense.SetBorder(value);
        }

        shell.WriteLine($"Давление границы: {defense.BorderPressure}/100 — {defense.BorderStatus()}");
    }
}

// swplague — управлять галактической эпидемией Минздрава (для теста).
//   swplague                          — показать текущий статус.
//   swplague seed [sectorId]          — заспавнить патоген (в случайном или указанном секторе);
//                                       если эпидемия уже идёт — подсыпать очаг в этот сектор.
//   swplague set <value> [sectorId]   — задать заражение всем занятым секторам (или одному).
//   swplague add <delta>              — прибавить/убавить заражение всем секторам.
//   swplague mutate                   — принудительная мутация (новый штамм).
//   swplague clear                    — ликвидировать эпидемию и обнулить очаги.
// Примеры:
//   swplague seed
//   swplague seed SWSectorKuat
//   swplague set 80
//   swplague set 60 SWSectorKuat
//   swplague add -25
[AdminCommand(AdminFlags.Debug)]
public sealed class SWPlagueCommand : IConsoleCommand
{
    public string Command => "swplague";
    public string Description => "Управлять галактической эпидемией: засеять, задать заражение, мутировать, очистить.";
    public string Help => "swplague [seed [sectorId] | set <value> [sectorId] | add <delta> | mutate | clear]";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var health = IoCManager.Resolve<IEntityManager>().System<SWMinistryHealthSystem>();

        if (args.Length < 1)
        {
            shell.WriteLine(health.AdminStatus());
            return;
        }

        switch (args[0].ToLowerInvariant())
        {
            case "seed":
                var seedSector = args.Length >= 2 ? args[1] : null;
                if (!health.ForceSeed(seedSector))
                    shell.WriteError("Не удалось засеять: нет занятых секторов или сектор не найден.");
                else
                    shell.WriteLine(health.AdminStatus());
                break;

            case "set":
                if (args.Length < 2 || !float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                {
                    shell.WriteError("swplague set <value> [sectorId]");
                    return;
                }

                var setSector = args.Length >= 3 ? args[2] : null;
                if (!health.SetInfection(setSector, value))
                    shell.WriteError("Не удалось: нет занятых секторов или сектор не найден.");
                else
                    shell.WriteLine(health.AdminStatus());
                break;

            case "add":
                if (args.Length < 2 || !float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var delta))
                {
                    shell.WriteError("swplague add <delta>");
                    return;
                }

                health.AddInfectionAll(delta);
                shell.WriteLine(health.AdminStatus());
                break;

            case "mutate":
                if (!health.ForceMutate())
                    shell.WriteError("Нет активной эпидемии для мутации.");
                else
                    shell.WriteLine(health.AdminStatus());
                break;

            case "clear":
                health.ClearPlague();
                shell.WriteLine("Эпидемия ликвидирована.");
                break;

            default:
                shell.WriteError(Help);
                break;
        }
    }
}

// swcase — управлять реестром дел Минюста (для теста).
//   swcase                  — показать статус (дел, нагрузка, ресурс, политика).
//   swcase add [тип]        — завести дело (тип: criminal|corruption|separatism|civil|political; без типа — случайный).
//   swcase backlog <0-100>  — задать нагрузку судов.
//   swcase clear            — очистить реестр дел.
// Примеры:
//   swcase add
//   swcase add corruption
//   swcase backlog 80
[AdminCommand(AdminFlags.Debug)]
public sealed class SWCaseCommand : IConsoleCommand
{
    public string Command => "swcase";
    public string Description => "Управлять реестром дел Минюста: завести дело, задать нагрузку, очистить.";
    public string Help => "swcase [add [type] | backlog <0-100> | clear]";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var justice = IoCManager.Resolve<IEntityManager>().System<SWMinistryJusticeSystem>();

        if (args.Length < 1)
        {
            shell.WriteLine(justice.AdminStatus());
            return;
        }

        switch (args[0].ToLowerInvariant())
        {
            case "add":
                SWCaseType? type = null;
                if (args.Length >= 2)
                {
                    if (!Enum.TryParse<SWCaseType>(args[1], true, out var parsed))
                    {
                        shell.WriteError("Тип: criminal | corruption | separatism | civil | political");
                        return;
                    }
                    type = parsed;
                }

                if (!justice.AdminAddCase(type))
                    shell.WriteError("Реестр заполнен (предел дел).");
                else
                    shell.WriteLine(justice.AdminStatus());
                break;

            case "backlog":
                if (args.Length < 2 || !float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                {
                    shell.WriteError("swcase backlog <0-100>");
                    return;
                }

                justice.SetBacklog(value);
                shell.WriteLine(justice.AdminStatus());
                break;

            case "clear":
                justice.ClearCases();
                shell.WriteLine("Реестр дел очищен.");
                break;

            default:
                shell.WriteError(Help);
                break;
        }
    }
}

// swpowers — управлять чрезвычайными полномочиями Канцлера (для теста).
//   swpowers                  — статус.
//   swpowers level <0-3>      — выдать/снять уровень полномочий.
//   swpowers grant <power>    — точечно выдать полномочие (veto/command/emergencybudget/martiallaw/mobilization/censorship/purge/decree/ministercontrol).
//   swpowers authority <0-100>  — авторитет/тирания.
//   swpowers legit <0-100>    — легитимность.
//   swpowers relinquish       — сложить полномочия.
[AdminCommand(AdminFlags.Debug)]
public sealed class SWPowersCommand : IConsoleCommand
{
    public string Command => "swpowers";
    public string Description => "Управлять чрезвычайными полномочиями Канцлера.";
    public string Help => "swpowers [level <0-3> | grant <power> | authority <0-100> | legit <0-100> | relinquish]";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var powers = IoCManager.Resolve<IEntityManager>().System<SWEmergencyPowersSystem>();

        if (args.Length < 1)
        {
            shell.WriteLine(powers.AdminStatus());
            return;
        }

        switch (args[0].ToLowerInvariant())
        {
            case "level":
                if (args.Length < 2 || !int.TryParse(args[1], out var lvl))
                {
                    shell.WriteError("swpowers level <0-3>");
                    return;
                }
                powers.AdminSetLevel(lvl);
                break;

            case "grant":
                if (args.Length < 2 || !Enum.TryParse<SWEmergencyPower>(args[1], true, out var power))
                {
                    shell.WriteError("power: veto|command|emergencybudget|martiallaw|mobilization|censorship|purge|decree|ministercontrol");
                    return;
                }
                powers.GrantPower(power);
                break;

            case "authority":
                if (args.Length < 2 || !float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var au))
                {
                    shell.WriteError("swpowers authority <0-100>");
                    return;
                }
                powers.AdminSetAuthority(au);
                break;

            case "legit":
                if (args.Length < 2 || !float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lg))
                {
                    shell.WriteError("swpowers legit <0-100>");
                    return;
                }
                powers.AdminSetLegitimacy(lg);
                break;

            case "relinquish":
                powers.Relinquish();
                break;

            default:
                shell.WriteError(Help);
                return;
        }

        shell.WriteLine(powers.AdminStatus());
    }
}

// swcorrupt — управлять коррупцией (для теста).
//   swcorrupt                          — статус (индекс).
//   swcorrupt sector <sectorId> <0-100>  — коррумпированность сектора.
//   swcorrupt person <имя...> <0-100>    — коррумпированность лица (имя может быть из нескольких слов).
//   swcorrupt account <имя...> <сумма>   — личный счёт лица.
[AdminCommand(AdminFlags.Debug)]
public sealed class SWCorruptCommand : IConsoleCommand
{
    public string Command => "swcorrupt";
    public string Description => "Задать коррупцию сектора/лица или личный счёт.";
    public string Help => "swcorrupt [sector <id> <0-100> | person <name> <0-100> | account <name> <amount>]";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var corruption = IoCManager.Resolve<IEntityManager>().System<SWCorruptionSystem>();

        if (args.Length < 1)
        {
            shell.WriteLine(corruption.AdminStatus());
            return;
        }

        if (args.Length < 3)
        {
            shell.WriteError(Help);
            return;
        }

        var mode = args[0].ToLowerInvariant();
        var key = string.Join(' ', args.Skip(1).Take(args.Length - 2));
        var raw = args[^1];

        switch (mode)
        {
            case "sector":
                if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var sv))
                {
                    shell.WriteError("swcorrupt sector <id> <0-100>");
                    return;
                }
                corruption.AdminSetSector(key, sv);
                break;

            case "person":
                if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var pv))
                {
                    shell.WriteError("swcorrupt person <name> <0-100>");
                    return;
                }
                corruption.AdminSetPerson(key, pv);
                break;

            case "account":
                if (!int.TryParse(raw, out var av))
                {
                    shell.WriteError("swcorrupt account <name> <amount>");
                    return;
                }
                corruption.AdminSetAccount(key, av);
                break;

            default:
                shell.WriteError("swcorrupt [sector|person|account] ...");
                return;
        }

        shell.WriteLine(corruption.AdminStatus());
    }
}

// swcrisis — заспавнить кризис вне зависимости от условий (для теста).
// Аргумент можно дать как ID сектора, так и ID кризиса — команда сама разберётся.
//   Республиканскому кризису сектор не нужен. Секторальному без сектора подберётся активный.
// Примеры:
//   swcrisis SWCrisisRepDepression              — республиканский кризис (без сектора)
//   swcrisis SWCrisisPlague                      — секторальный кризис в случайном активном секторе
//   swcrisis SWSectorKuat SWCrisisPlague         — секторальный кризис конкретному сектору
//   swcrisis SWSectorKuat                        — случайный кризис Куату
[AdminCommand(AdminFlags.Debug)]
public sealed class SWCrisisCommand : IConsoleCommand
{
    public string Command => "swcrisis";
    public string Description => "Заспавнить кризис (по ID кризиса и/или сектора, в любом порядке).";
    public string Help => "swcrisis <sectorId|crisisId> [crisisId]";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 1)
        {
            shell.WriteError(Help);
            return;
        }

        var entMan = IoCManager.Resolve<IEntityManager>();
        var proto = IoCManager.Resolve<IPrototypeManager>();
        var crisis = entMan.System<SWSenateCrisisSystem>();

        string? sectorId;
        string? crisisId;

        // Allow the crisis id as the first argument (handy for sector-less republic crises).
        if (proto.HasIndex<SWCrisisPrototype>(args[0]))
        {
            crisisId = args[0];
            sectorId = args.Length >= 2 ? args[1] : null;
        }
        else
        {
            sectorId = args[0];
            crisisId = args.Length >= 2 ? args[1] : null;
        }

        if (crisis.AdminSpawn(sectorId, crisisId))
            shell.WriteLine("Кризис запущен.");
        else
            shell.WriteError("Не удалось: проверьте ID сектора/кризиса.");
    }
}
