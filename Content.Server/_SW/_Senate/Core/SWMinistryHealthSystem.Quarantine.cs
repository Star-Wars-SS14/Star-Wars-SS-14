using System.Linq;
using Content.Server._SW._Senate.Economy.Simulation;
using Content.Server._SW._Senate.Debate;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Ministries;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.Enums;
using Robust.Shared.Player;

namespace Content.Server._SW._Senate.Core;

public sealed partial class SWMinistryHealthSystem
{
    [Dependency] private readonly SWSenateFloorSystem _healthFloor = default!;
    private sealed class Quarantine(int id, string label, string[] worlds, int hours, bool humanitarian, EntityUid requester)
    {
        public readonly int Id = id;
        public readonly string Label = label;
        public readonly string[] Worlds = worlds;
        public readonly int Hours = hours;
        public readonly bool Humanitarian = humanitarian;
        public readonly EntityUid Requester = requester;
        public bool Pending = true;
        public double Until;
    }
    private readonly List<Quarantine> _quarantines = new();
    private int _nextQuarantine;
    // The former galaxy-wide control is retired. Every approval applies to one immutable request.
    public bool EmergencyActive => _quarantines.Any(q => !q.Pending && q.Until > _time.ActiveGameHours);
    public bool EmergencyPending => false;
    public double EmergencyHoursLeft => _quarantines.Where(q => !q.Pending).Select(q => Math.Max(0, q.Until - _time.ActiveGameHours)).DefaultIfEmpty(0).Max();
    public void DeclareEmergency() { }
    public bool ApproveEmergency() => false;
    public bool RejectEmergency() => false;
    public bool SetQuarantine(string sectorId, bool on) => false;
    public bool IsQuarantined(string sectorId) => _worlds.Values.Any(w => w.Map.Sector?.Id == sectorId && WorldQuarantined(w.Map.Id));
    private bool WorldQuarantined(string world) => _quarantines.Any(q => !q.Pending && q.Until > _time.ActiveGameHours && q.Worlds.Contains(world));
    private bool QuarantineBlocksContact(string from, string to) => _quarantines.Any(q => !q.Pending && q.Until > _time.ActiveGameHours && q.Worlds.Contains(from) != q.Worlds.Contains(to));
    private void ResetQuarantines() { _quarantines.Clear(); _nextQuarantine = 0; PublishQuarantines(); }
    private void PublishQuarantines() => _economy.SetQuarantineTransitRestrictions(_quarantines
        .Where(q => !q.Pending && q.Until > _time.ActiveGameHours)
        .Select(q => new EconomyQuarantineRule($"health:{q.Id}", q.Worlds.ToArray(), q.Humanitarian)).ToArray());

    private bool RequestQuarantine(string worldId, string scope, int hours, bool humanitarian, EntityUid requester)
    {
        if (!CanManage || hours is not (2 or 4 or 6) || !_worlds.TryGetValue(worldId, out var target) || _quarantines.Count >= 12) return false;
        if (scope is not ("" or "system" or "sector")) return false;
        var worlds = new[] { worldId };
        var label = target.Map.Name;
        if (scope == "system")
        {
            if (string.IsNullOrEmpty(target.Map.SystemId)) return false;
            worlds = _worlds.Values.Where(w => w.Map.SystemId == target.Map.SystemId).Select(w => w.Map.Id).Order().ToArray();
            if (worlds.Length < 2) return false;
            label = string.IsNullOrEmpty(target.Map.SystemName) ? "Система " + target.Map.Name : target.Map.SystemName;
        }
        else if (scope == "sector")
        {
            if (target.Map.Sector is not { } sector) return false;
            worlds = _worlds.Values.Where(w => w.Map.Sector == sector).Select(w => w.Map.Id).Order().ToArray();
            label = "Сектор " + _proto.Index(sector).Name;
        }
        if (_quarantines.Any(q => q.Worlds.Intersect(worlds).Any())) return false;
        var request = new Quarantine(++_nextQuarantine, label, worlds, hours, humanitarian, requester);
        _quarantines.Add(request);
        if (_senate.OfficesVacant)
        {
            ApproveQuarantine(request, "Автопилот Сената");
            return true;
        }
        var recipient = _senate.Chancellor != null ? "канцлера" : "спикера";
        AddLog($"На рассмотрение {recipient} направлен карантин: {label}, {hours} ч.");
        RaiseLocalEvent(new SWSenateCustomAnnouncementEvent("Министерство здравоохранения",
            $"На рассмотрение {recipient} направлен карантин: {label}.", SWSenateAnnouncementKind.Health));
        return true;
    }
    /// <summary>The work console additionally verifies the corresponding office card.</summary>
    public bool CanDecideQuarantine(EntityUid actor)
    {
        var holder = _senate.Chancellor ?? _senate.Speaker;
        var entity = _senate.Chancellor != null ? _healthFloor.Chancellor : _healthFloor.Speaker;
        return _time.ModeActive && _time.DayActive && holder != null && entity == actor && holder == Name(actor)
            && TryComp<ActorComponent>(actor, out var player) && player.PlayerSession.Status == SessionStatus.InGame
            && player.PlayerSession.AttachedEntity == actor
            && TryComp<MobStateComponent>(actor, out var mob) && mob.CurrentState == MobState.Alive;
    }
    public bool DecideQuarantine(int id, bool approve, EntityUid actor)
    {
        if (!CanDecideQuarantine(actor)) return false;
        var request = _quarantines.FirstOrDefault(q => q.Id == id && q.Pending);
        if (request == null || request.Requester == actor) return false;
        if (!approve)
        {
            _quarantines.Remove(request);
            AddLog($"Карантин отклонён: {request.Label}.");
            return true;
        }
        ApproveQuarantine(request, _senate.Chancellor != null ? "Канцлер" : "Спикер");
        return true;
    }
    private void ProcessQuarantineAutopilot()
    {
        if (!_time.ModeActive || !_time.DayActive || !_senate.OfficesVacant) return;
        foreach (var request in _quarantines.Where(q => q.Pending).ToArray())
            ApproveQuarantine(request, "Автопилот Сената");
    }
    private void ApproveQuarantine(Quarantine request, string approver)
    {
        if (!request.Pending) return;
        request.Pending = false;
        request.Until = _time.ActiveGameHours + request.Hours;
        PublishQuarantines();
        AddLog($"{approver}: карантин одобрен, {request.Label}, {request.Hours} ч.");
        RaiseLocalEvent(new SWSenateCustomAnnouncementEvent(approver,
            $"Введён карантин: {request.Label}, на {request.Hours} игровых ч. {(request.Humanitarian ? "Входящие поставки пищи и лекарств разрешены." : "Обычные поставки остановлены.")} Конвои Минздрава проходят.", SWSenateAnnouncementKind.Health));
    }
    public bool LiftQuarantine(int id)
    {
        if (!_time.ModeActive || !_time.DayActive) return false;
        var request = _quarantines.FirstOrDefault(q => q.Id == id);
        if (request == null) return false;
        _quarantines.Remove(request); PublishQuarantines();
        AddLog($"Карантин снят: {request.Label}.");
        RaiseLocalEvent(new SWSenateCustomAnnouncementEvent("Министерство здравоохранения", $"Карантин снят: {request.Label}.", SWSenateAnnouncementKind.Health));
        return true;
    }
    private void ExpireQuarantines()
    {
        foreach (var q in _quarantines.Where(q => !q.Pending && q.Until <= _time.ActiveGameHours).ToArray())
        { _quarantines.Remove(q); AddLog($"Срок карантина истёк: {q.Label}."); }
        // Restriction equality is checked by the runner; an expired source cannot persist at a day boundary.
        PublishQuarantines();
    }
    public List<SWHealthQuarantineView> BuildQuarantines() => _quarantines.Select(q =>
    {
        var boundaries = _links.Where(l => q.Worlds.Contains(l.From) != q.Worlds.Contains(l.To)).ToArray();
        var food = q.Worlds.Select(w => StockHours(w, "nutrition")).DefaultIfEmpty(-1).Min();
        var care = q.Worlds.Select(w => StockHours(w, "care_drugs")).DefaultIfEmpty(-1).Min();
        return new SWHealthQuarantineView
        {
            Id = q.Id, Label = q.Label, WorldIds = q.Worlds.ToList(), Pending = q.Pending, Humanitarian = q.Humanitarian,
            HoursLeft = q.Pending ? q.Hours : Math.Max(0, q.Until - _time.ActiveGameHours),
            Consequences = $"Обычный импорт и экспорт: {(q.Pending ? "будут остановлены после одобрения" : "остановлены")}. Переходов: {boundaries.Length}. "
                + (q.Worlds.Length > 1 ? "Внутренние перевозки сохраняются. " : "")
                + $"Минимальный запас продуктов: {(food < 0 ? "нет данных" : $"{food:0.0} ч")}; лекарств: {(care < 0 ? "нет данных" : $"{care:0.0} ч")}. "
                + (q.Humanitarian ? "Входящие поставки пищи и лекарств разрешены. " : "Обычный гуманитарный коридор закрыт. ")
                + "Конвои Минздрава проходят; военная блокада остаётся в силе.",
            Routes = boundaries.Select(l =>
            {
                var edge = _economy.Snapshot?.Edges.FirstOrDefault(e => e.Id == l.ID);
                return new SWMapCrisisRouteEntry { EdgeId = l.ID, BaseCapacity = l.CapacitySlotsPerHour,
                    CapacityMultiplier = q.Pending ? 1 : 0, HasUsage = edge != null, UsedSlotsPerHour = (edge?.UsedSlotsLastStep ?? 0) / RepublicEconomyModel.StepHours };
            }).ToList(),
        };
    }).ToList();
}
