using Content.Shared._SW._Senate.Director;
using Content.Shared._SW._Senate.Ministries;
using System.Linq;
using Robust.Shared.Random;

namespace Content.Server._SW._Senate.Director;

public sealed partial class SWDirectorSystem
{
    private bool CreateTacticalPressure(string sectorId, bool blockade, int cost, ref string result)
    {
        if (!_defense.MinisterActive)
        {
            result = "Министр обороны отсутствует; военные события приостановлены.";
            return false;
        }
        if (_influence < cost)
        {
            result = $"Недостаточно влияния: нужно {cost}.";
            return false;
        }
        var map = _defense.BuildMapState(includeOrderOptions: false);
        var sectors = map.Borders.Where(b => b.Faction == "empire")
            .Select(b => map.Worlds.First(w => w.Id == b.RepublicWorldId).SectorId)
            .Where(s => sectorId.Length == 0 || s == sectorId).Distinct().ToList();
        while (sectors.Count > 0)
        {
            var selected = _random.Pick(sectors);
            sectors.Remove(selected);
            if (!_defense.TryCreateImperialIncident(selected, blockade)) continue;
            _influence -= cost;
            return true;
        }
        result = "На выбранной границе нет места для нового события.";
        return false;
    }

    public bool EditDefenseEvent(SWDefenseDirectorEventMessage message)
    {
        if (Mode != SWDirectorMode.Manual || !_time.ModeActive || !_time.DayActive)
        {
            LastNotice = "Редактирование доступно в ручном режиме в рабочее время Сената.";
            return false;
        }

        var success = message.Resolve
            ? _defense.ResolveDirectorEvent(message.EventId)
            : _defense.ConfigureDirectorEvent(message);
        LastNotice = success ? (message.Resolve ? "Событие завершено." : "Событие сохранено.")
            : "Событие отклонено: проверьте место, параметры и наличие действующего министра обороны.";
        if (success)
            AddChronicle($"тактическое событие {(message.Resolve ? "завершено" : "изменено")}: {Shorten(message.Title)}");
        Updated?.Invoke();
        return success;
    }
}
