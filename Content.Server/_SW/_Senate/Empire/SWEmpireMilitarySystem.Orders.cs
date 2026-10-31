using System;
using System.Linq;
using Content.Server._SW._Senate.Economy;
using Content.Server._SW._Senate.Economy.Simulation;
using Content.Shared._SW._Senate.Economy;
using Content.Shared._SW._Senate.Empire;

namespace Content.Server._SW._Senate.Empire;

public sealed partial class SWEmpireMilitarySystem
{
    private bool Yard(string world) => Own(world) && _proto.TryIndex<SWPlanetEconomyPrototype>(world, out var p)
        && p.Facilities.Any(f => f.Id == "shipyard");

    private (string Status, string Details) SupplyView(Fleet fleet)
    {
        if (!fleet.Alive) return ("Соединение утрачено", string.Empty);
        if (fleet.Next != null) return ("В пути", "Пополнение доступно в порту.");
        if (!Imperial(fleet.World)) return ("Вне зоны снабжения", "Для пополнения вернитесь в имперский порт.");
        if (!Own(fleet.World)) return ("Вне зоны снабжения", "На этом мире нет действующего порта снабжения.");
        if (fleet.Supply > 60) return ("Запаса достаточно", "Новый комплект будет заказан при запасе 60 или ниже. Базовый расход на позиции: 3 пункта за активный час.");
        if (_economy.Runner?.Fault != null) return ("Снабжение недоступно", "Порт не может обработать заказ.");
        if (!_supplyPorts.TryGetValue(fleet.World, out var id)) return ("Ожидание заявки", "Порт ещё не принял заказ.");
        var project = _economy.GetProject(id);
        if (project == null) return ("Заказ принят", "Порт принял заказ на снабжение.");
        if (project.Status == EconomyProjectStatus.Completed)
            return ("Комплект готов", "Готовый комплект получит одно соединение в порту.");
        if (project.Status == EconomyProjectStatus.Working)
            return ("Сборка комплекта", $"Материалы доставлены; работа {project.WorkDone:0.##}/{project.WorkHours:0.##} ч.");
        var lines = project.Materials.Select(m =>
        {
            var name = _economy.GoodNames.GetValueOrDefault(m.Good, m.Good);
            var arrival = m.NextDeliveryHours is { } h ? $", ближайшая часть через {h:0.##} эконом. ч" : string.Empty;
            return $"{name}: {m.Delivered:0.###}/{m.Required:0.###}, в пути {m.Incoming:0.###}{arrival}";
        });
        var status = project.Materials.Any(m => m.DeliveryStatus == "route_blocked") ? "Нет источника или доступного маршрута"
            : project.Materials.Any(m => m.DeliveryStatus == "fuel") ? "Ожидание топлива для доставки"
            : project.Materials.Any(m => m.DeliveryStatus is "waiting_transport" or "transshipment") ? "Ожидание транспорта"
            : project.Materials.Any(m => m.Incoming > 0) ? "Материалы в пути" : "Ожидание материалов";
        return (status, string.Join("\n", lines));
    }

    public bool TryQuote(SWEmpireMilitaryRequest request, out SWEmpireMilitaryQuote quote)
    {
        quote = new SWEmpireMilitaryQuote();
        EnsureInitialized();
        if (request == null || !Enum.IsDefined(request.Action)) { quote.Reason = "Неизвестный приказ"; return false; }
        if (string.IsNullOrWhiteSpace(request.RequestId) || request.RequestId.Length > 128 || request.Target == null || request.Target.Length > 128)
        { quote.Reason = "Некорректные данные приказа"; return false; }
        if (_requests.Contains(request.RequestId)) { quote.Reason = "Этот приказ уже был принят"; return false; }
        if (!_time.ModeActive || !_time.DayActive) { quote.Reason = "Штаб закрыт: приказы доступны в активное время"; return false; }
        if (request.Action == SWEmpireMilitaryAction.Recruit)
        {
            quote.Cost = 5000;
            if (!Yard(request.Target)) { quote.Reason = "На выбранном мире нет действующей имперской верфи"; return false; }
            if (_construction.Values.Contains(request.Target)) { quote.Reason = "Верфь уже формирует соединение"; return false; }
            if (_fleets.Values.Count(f => f.Alive) + _construction.Count >= 8) { quote.Reason = "Достигнут предел соединений"; return false; }
            quote.Allowed = true; quote.Reason = "Формирование требует кораблей и материалов"; return true;
        }
        if (!_fleets.TryGetValue(request.FleetId, out var fleet)) { quote.Reason = "Соединение не найдено"; return false; }
        if (!fleet.Alive) { quote.Reason = "Соединение утрачено"; return false; }
        if (fleet.World == fleet.CivilianBase && !Imperial(fleet.World) &&
            (request.Action is SWEmpireMilitaryAction.BorderConflict or SWEmpireMilitaryAction.Raid or SWEmpireMilitaryAction.Blockade))
        { quote.Reason = "Мирное базирование не разрешает боевые операции"; return false; }
        if (fleet.WorkProject != null) { quote.Reason = "Соединение находится на ремонте"; return false; }
        if (fleet.DepositPending != null)
        { quote.Reason = "Дождитесь подтверждения разгрузки"; return false; }
        if (fleet.RaidPending != null && request.Action != SWEmpireMilitaryAction.Return)
        { quote.Reason = "Дождитесь подтверждения погрузки; отход можно приказать заранее"; return false; }
        if (request.Action == SWEmpireMilitaryAction.Escort)
        {
            if (!request.Target.StartsWith("fleet:", StringComparison.Ordinal) || !int.TryParse(request.Target.AsSpan(6), out var leaderId)
                || leaderId == fleet.Id || !_fleets.TryGetValue(leaderId, out var leader) || !leader.Alive)
            { quote.Reason = "Выберите другое действующее соединение"; return false; }
            if (leader.EscortLeader != 0) { quote.Reason = "Выбранное соединение само сопровождает другой флот"; return false; }
            if (fleet.EventId != 0) { quote.Reason = "Соединение уже участвует в пограничном конфликте"; return false; }
            if (fleet.Next != null) { quote.Reason = "Дождитесь завершения перехода"; return false; }
            if (Imperial(fleet.World) && Imperial(leader.World))
            {
                if (OwnPath(fleet.World, leader.World) == null) { quote.Reason = "Нет доступного пути к выбранному соединению"; return false; }
            }
            else if (Imperial(fleet.World) && Republic(leader.World))
            {
                var crossing = _links.FirstOrDefault(l => l.ID == leader.Crossing);
                if (crossing == null || !CrossingOpen(crossing.ID)) { quote.Reason = "Пограничный переход ещё не открыт для подкреплений"; return false; }
                var homeSide = Imperial(crossing.From) ? crossing.From : crossing.To;
                var foreignSide = homeSide == crossing.From ? crossing.To : crossing.From;
                if (OwnPath(fleet.World, homeSide) == null || RepublicPath(foreignSide, leader.World, true) == null)
                { quote.Reason = "Нет доступного пути к выбранному соединению"; return false; }
            }
            else if (Republic(fleet.World) && Republic(leader.World))
            {
                if (!CrossingOpen(fleet.Crossing) || RepublicPath(fleet.World, leader.World, true) == null)
                { quote.Reason = "Нет доступного пути к выбранному соединению"; return false; }
            }
            else { quote.Reason = "Нет доступного пути к выбранному соединению"; return false; }
            quote.Allowed = true; quote.Reason = "Соединение следует за ведущим и участвует в его бою"; return true;
        }
        if (request.Action == SWEmpireMilitaryAction.Raid && Republic(fleet.World) && fleet.Next == null)
        {
            if (_diplomacy.BlocksHostileAction(request.Target, out var treatyReason) ||
                _diplomacy.BlocksHostileAction(fleet.World, out treatyReason) ||
                fleet.Crossing != null && _diplomacy.BlocksHostileTransit(fleet.Crossing, out treatyReason))
            { quote.Reason = treatyReason; return false; }
            if (!_defense.ImperialThreatsEnabled)
            { quote.Reason = "Имперские угрозы отключены режимом директора"; return false; }
            if (fleet.Permit == null) { quote.Reason = "Пограничный переход больше не удерживается"; return false; }
            if (!CrossingPassable(fleet.Crossing)) { quote.Reason = "На переходе идёт бой; дождитесь победы"; return false; }
            if (!_defense.ImperialOperationsAllowed) { quote.Reason = "Министр обороны Республики отсутствует или не управляется игроком"; return false; }
            if (fleet.RaidPending != null) { quote.Reason = "Дождитесь подтверждения погрузки"; return false; }
            if (fleet.CargoAmount > 1e-9) { quote.Reason = "Сначала доставьте захваченный груз в имперский порт"; return false; }
            if (request.Target != fleet.World && RepublicPath(fleet.World, request.Target) == null)
            { quote.Reason = "Нет доступного маршрута к выбранному миру"; return false; }
            if (request.Target != fleet.World && RepublicPath(fleet.World, request.Target) is { } raidPath && TreatyBlocksPath(fleet.World, raidPath))
            { quote.Reason = "На маршруте действует локальное перемирие"; return false; }
            quote.Allowed = true; quote.Reason = "Маршрут проходит по существующим линиям"; return true;
        }
        if (request.Action == SWEmpireMilitaryAction.Return && Republic(fleet.World) && fleet.Next == null && fleet.Crossing != null && fleet.World != fleet.CivilianBase)
        {
            var exit = _links.FirstOrDefault(l => l.ID == fleet.Crossing);
            var foreign = exit == null ? null : Republic(exit.From) ? exit.From : exit.To;
            if (exit == null || foreign == null || RepublicPath(fleet.World, foreign, true) == null)
            { quote.Reason = "Нет известного обратного перехода"; return false; }
            quote.Allowed = true;
            quote.Reason = "Отход по ранее пройденному переходу"; return true;
        }
        if (request.Action is SWEmpireMilitaryAction.BorderConflict or SWEmpireMilitaryAction.Raid or SWEmpireMilitaryAction.Blockade)
        {
            var crossing = BorderAt(fleet.World, request.Target);
            if (!_defense.ImperialThreatsEnabled)
            { quote.Reason = "Имперские угрозы отключены режимом директора"; return false; }
            if (fleet.Next != null) { quote.Reason = "Дождитесь завершения перехода"; return false; }
            if (crossing == null) { quote.Reason = "От текущего мира нет пограничной линии к выбранной цели"; return false; }
            if (_diplomacy.BlocksHostileAction(request.Target, out var treatyReason) ||
                _diplomacy.BlocksHostileAction(fleet.World, out treatyReason) ||
                _diplomacy.BlocksHostileTransit(crossing.ID, out treatyReason))
            { quote.Reason = treatyReason; return false; }
            if (!_defense.ImperialOperationsAllowed) { quote.Reason = "Министр обороны Республики отсутствует или не управляется игроком"; return false; }
            if (fleet.Supply < 15) { quote.Reason = $"Снабжение {fleet.Supply:0.#}; необходимо не менее 15"; return false; }
            if (request.Action == SWEmpireMilitaryAction.BorderConflict && fleet.EventId != 0)
            { quote.Reason = "Соединение уже участвует в пограничном конфликте"; return false; }
            if (request.Action == SWEmpireMilitaryAction.BorderConflict && _fleets.Values.Count(f => f.EventId != 0) >= 2)
            { quote.Reason = "Одновременно допустимо не более двух пограничных конфликтов"; return false; }
            if (request.Action == SWEmpireMilitaryAction.BorderConflict && _fleets.Values.Any(f => f.EventId != 0 && f.Crossing == crossing.ID))
            { quote.Reason = "На этом переходе уже идёт пограничный конфликт"; return false; }
            if (request.Action != SWEmpireMilitaryAction.BorderConflict && !CrossingPassable(crossing.ID))
            { quote.Reason = CrossingOpen(crossing.ID) ? "На переходе идёт бой; дождитесь победы" : "Сначала необходимо выиграть конфликт на этом переходе"; return false; }
            quote.Allowed = true; quote.Reason = request.Action == SWEmpireMilitaryAction.BorderConflict
                ? "Противник получит предупреждение и время на ответ" : "Переход открыт для этого соединения";
            return true;
        }
        if (request.Action is SWEmpireMilitaryAction.Repair)
        {
            quote.Cost = 1500;
            if (fleet.Next != null) { quote.Reason = "Дождитесь завершения перехода"; return false; }
            if (fleet.EventId != 0) { quote.Reason = "Сначала завершите пограничный конфликт"; return false; }
            if (!Yard(fleet.World)) { quote.Reason = "Для ремонта нужна действующая имперская верфь"; return false; }
            if (fleet.Damage <= 0 && fleet.Strength >= 100) { quote.Reason = "Соединение не нуждается в ремонте"; return false; }
            quote.Allowed = true; quote.Reason = "Ремонт требует физических материалов"; return true;
        }
        if (request.Action is not (SWEmpireMilitaryAction.Move or SWEmpireMilitaryAction.Guard
            or SWEmpireMilitaryAction.Patrol or SWEmpireMilitaryAction.Recon or SWEmpireMilitaryAction.Return or SWEmpireMilitaryAction.Train))
        { quote.Reason = "Приказ пока недоступен"; return false; }
        var target = request.Action == SWEmpireMilitaryAction.Return ? fleet.Home : request.Target;
        var path = request.Action is SWEmpireMilitaryAction.Move or SWEmpireMilitaryAction.Return
            ? PeacefulPath(fleet, fleet.Next ?? fleet.World, target, request.Action == SWEmpireMilitaryAction.Return)
            : OwnPath(fleet.Next ?? fleet.World, target);
        if (path == null) { quote.Reason = "Нет доступного пути по территории Империи"; return false; }
        if (fleet.Supply < 15 && request.Action != SWEmpireMilitaryAction.Return)
        { quote.Reason = $"Снабжение {fleet.Supply:0.#}; необходимо не менее 15"; return false; }
        var current = fleet.Next ?? fleet.World;
        double eta = fleet.Next == null ? 0 : Math.Max(0, fleet.Until - _time.ActiveGameHours);
        foreach (var next in path) { eta += Math.Clamp(Link(current, next)!.TravelHours * .4, .1, .3); current = next; }
        quote.EtaHours = eta; quote.Allowed = true; quote.Reason = "Снабжение расходуется во время перехода"; return true;
    }

    public bool TrySubmit(SWEmpireMilitaryRequest request, out string reason)
    {
        if (!TryQuote(request, out var quote)) { reason = quote.Reason; return false; }
        if (request.Action == SWEmpireMilitaryAction.Recruit)
        {
            var id = $"imperial-navy-construction-{_nextProject++}";
            if (!_economy.QueueNavyProject(id, request.Target, SWNavyProjectKind.Construction))
            { reason = "Верфь не приняла заказ"; return false; }
            _construction[id] = request.Target;
        }
        else if (request.Action == SWEmpireMilitaryAction.Escort)
        {
            var fleet = _fleets[request.FleetId]; fleet.EscortLeader = int.Parse(request.Target.AsSpan(6));
            fleet.Path.Clear(); fleet.Order = request.Action; fleet.Target = request.Target;
        }
        else if (request.Action == SWEmpireMilitaryAction.BorderConflict)
        {
            var fleet = _fleets[request.FleetId]; var edge = BorderAt(fleet.World, request.Target)!;
            if (!_defense.TryStartImperialCrossing(fleet.Id.ToString(), edge.ID, out var eventId, out reason)) return false;
            fleet.EventId = eventId; fleet.Crossing = edge.ID; fleet.Permit = null; fleet.Order = request.Action;
        }
        else if (request.Action == SWEmpireMilitaryAction.Raid && Republic(_fleets[request.FleetId].World))
        {
            var fleet = _fleets[request.FleetId]; var path = request.Target == fleet.World ? new System.Collections.Generic.List<string>() : RepublicPath(fleet.World, request.Target)!;
            fleet.Path.Clear(); foreach (var step in path) fleet.Path.Enqueue(step);
            fleet.Target = path.Count == 0 ? fleet.World : path.Last(); fleet.Order = request.Action; fleet.RaidAttempted = false; fleet.RaidReadyAt = 0;
            if (fleet.Contact && path.Count == 0)
            { _defense.PromoteImperialRaid(fleet.RaidEventId); fleet.Contact = false; fleet.ContactWorld = null; }
        }
        else if (request.Action is SWEmpireMilitaryAction.Raid or SWEmpireMilitaryAction.Blockade)
        {
            var fleet = _fleets[request.FleetId]; var edge = BorderAt(fleet.World, request.Target)!;
            fleet.Crossing = edge.ID; fleet.Permit = edge.ID;
            fleet.Next = edge.From == fleet.World ? edge.To : edge.From;
            fleet.LegStartedAt = _time.ActiveGameHours; fleet.Until = fleet.LegStartedAt + Math.Clamp(edge.TravelHours * .4, .1, .3);
            fleet.Target = fleet.Next; fleet.Order = request.Action; fleet.RaidAttempted = false; fleet.RaidReadyAt = 0;
        }
        else if (request.Action == SWEmpireMilitaryAction.Return && Republic(_fleets[request.FleetId].World) && _fleets[request.FleetId].World != _fleets[request.FleetId].CivilianBase)
        {
            var fleet = _fleets[request.FleetId]; var edge = _links.First(l => l.ID == fleet.Crossing);
            if (fleet.EventId != 0) ReleaseCrossing(fleet);
            var foreign = Republic(edge.From) ? edge.From : edge.To;
            var own = foreign == edge.From ? edge.To : edge.From;
            fleet.Path.Clear(); foreach (var step in RepublicPath(fleet.World, foreign, true)!) fleet.Path.Enqueue(step);
            fleet.Path.Enqueue(own); fleet.Target = own; fleet.Order = request.Action; fleet.Permit = null;
        }
        else if (request.Action == SWEmpireMilitaryAction.Repair)
        {
            var fleet = _fleets[request.FleetId]; var id = $"imperial-navy-repair-{_nextProject++}";
            if (!_economy.QueueNavyProject(id, fleet.World, SWNavyProjectKind.Repair, fleet.Id))
            { reason = "Верфь не приняла заказ"; return false; }
            fleet.WorkProject = id; fleet.Order = request.Action;
        }
        else
        {
            var fleet = _fleets[request.FleetId];
            fleet.EscortLeader = 0;
            if (fleet.EventId != 0)
            {
                ReleaseCrossing(fleet);
                fleet.Crossing = null;
            }
            var target = request.Action == SWEmpireMilitaryAction.Return ? fleet.Home : request.Target;
            var path = request.Action is SWEmpireMilitaryAction.Move or SWEmpireMilitaryAction.Return
                ? PeacefulPath(fleet, fleet.Next ?? fleet.World, target, request.Action == SWEmpireMilitaryAction.Return)!
                : OwnPath(fleet.Next ?? fleet.World, target)!;
            if (request.Action == SWEmpireMilitaryAction.Move && !Imperial(target)) fleet.CivilianBase = target;
            fleet.Path.Clear(); foreach (var step in path) fleet.Path.Enqueue(step);
            fleet.Target = target; fleet.Order = request.Action;
        }
        if (request.Action != SWEmpireMilitaryAction.Escort && _fleets.TryGetValue(request.FleetId, out var changed))
        { changed.EscortLeader = 0; changed.BlockedLeg = null; }
        _requests.Add(request.RequestId); reason = "Приказ принят"; return true;
    }

    public SWEmpireMilitaryState BuildState()
    {
        EnsureInitialized();
        var state = new SWEmpireMilitaryState { NowHours = _time.ActiveGameHours, ClockRunning = _time.ModeActive && _time.DayActive };
        state.Battles = BuildBattles(true, _defense.BuildMapState(false));
        var supply = SWRepublicNavySupplyPublication.Create(_economy.Snapshot, _economy.GoodNames);
        state.SupplyHour = supply.Hour; state.SupplyFlows = supply.Flows.Where(f => Own(f.From) && Own(f.To) && Own(f.TargetWorldId)).ToList();
        foreach (var observation in _observations.Values)
            state.Observations.Add(new SWEmpireMilitaryObservationView { SectorId = observation.SectorId, Summary = observation.Summary,
                SeenHours = observation.SeenHours, Current = _fleets.Values.Any(f => f.Alive && f.Next == null
                    && f.Order == SWEmpireMilitaryAction.Recon && Imperial(f.World)
                    && Link(f.World, observation.SectorId) != null) });
        foreach (var edge in _links)
        {
            var own = Imperial(edge.From) && Republic(edge.To) ? edge.From : Imperial(edge.To) && Republic(edge.From) ? edge.To : null;
            if (own == null) continue;
            var foreign = edge.From == own ? edge.To : edge.From;
            var sector = PublicWorld(foreign);
            var fleet = _fleets.Values.FirstOrDefault(f => f.Alive && f.Crossing == edge.ID && f.EventId != 0);
            state.Borders.Add(new SWEmpireMilitaryBorderView { Id = edge.ID,
                Name = _worlds[own].Name + " — " + _worlds[foreign].Name, OwnWorldId = own, ForeignSectorId = sector,
                Status = fleet == null ? "Нет конфликта" : !_defense.ImperialOperationsAllowed ? "Операция приостановлена"
                    : _defense.ImperialCrossingReady(fleet.EventId) && _defense.GetImperialDefenders(fleet.EventId).Count > 0 ? "Бой на границе"
                    : fleet.Permit != null ? "Переход удерживается"
                    : _defense.ImperialCrossingBattleStarted(fleet.EventId) ? "Бой на границе" : "Ожидание ответа Республики",
                Warning = fleet != null && !_defense.ImperialOperationsAllowed ? "Министр обороны Республики отсутствует" : string.Empty,
                ReadyAt = fleet != null && fleet.Permit == null ? _defense.ImperialCrossingReadyAt(fleet.EventId) : null });
        }
        foreach (var fleet in _fleets.Values)
        {
            var supplyStatus = SupplyView(fleet);
            state.Fleets.Add(new SWEmpireMilitaryFleetView { Id = fleet.Id, Name = fleet.Name, WorldId = PublicWorld(fleet.World),
                NextWorldId = fleet.Next == null ? string.Empty : PublicWorld(fleet.Next), Target = PublicWorld(fleet.Target), Order = fleet.Order, Strength = fleet.Strength,
                Damage = fleet.Damage, Supply = fleet.Supply, Training = fleet.Training, EtaHours = fleet.Until,
                Route = fleet.Path.Select(PublicWorld).ToList(), LegStartedAt = fleet.LegStartedAt, LegEndsAt = fleet.Until,
                CargoGood = fleet.CargoGood ?? string.Empty, CargoGoodName = fleet.CargoGood == null ? string.Empty : _economy.GoodNames.GetValueOrDefault(fleet.CargoGood, fleet.CargoGood),
                CargoAmount = fleet.CargoAmount, CargoCapacity = RaidCargoSlots,
                CargoUsedSlots = fleet.CargoAmount * (_economy.Definition?.Goods.FirstOrDefault(g => g.Id == fleet.CargoGood)?.CargoSlots ?? 0),
                CargoReadyAt = fleet.RaidReadyAt > _time.ActiveGameHours ? fleet.RaidReadyAt : null,
                SupplyStatus = supplyStatus.Status, SupplyDetails = supplyStatus.Details,
                CargoStatus = fleet.RaidPending != null ? "Изъятие подтверждается" : fleet.DepositPending != null ? "Разгрузка подтверждается"
                    : fleet.RaidReadyAt > _time.ActiveGameHours ? "Подготовка захвата груза"
                    : fleet.CargoAmount > 0 && fleet.Next == null && Own(fleet.World) ? "Груз на борту; ожидание места на складе"
                    : fleet.CargoAmount > 0 ? "Груз на борту"
                    : fleet.RaidAttempted && fleet.Order == SWEmpireMilitaryAction.Raid ? "Доступный груз не обнаружен" : string.Empty,
                Phase = !fleet.Alive ? "Утрачен" : fleet.WorkProject != null ? "Ремонт" : fleet.Next != null ? "Переход"
                    : RaidCombatVisible(fleet) ? "Бой"
                    : fleet.EventId != 0 && !_defense.ImperialOperationsAllowed ? "Приостановлено"
                    : fleet.EventId != 0 && _defense.ImperialCrossingReady(fleet.EventId) && _defense.GetImperialDefenders(fleet.EventId).Count > 0 ? "Бой"
                    : fleet.EventId != 0 && fleet.Permit == null ? "Ожидание ответа" : "На позиции",
                Status = !fleet.Alive ? "Соединение утрачено" : fleet.WorkProject != null ? "Ремонт" : fleet.Next != null ? "Гиперпереход"
                    : fleet.World == fleet.CivilianBase && !Imperial(fleet.World) && fleet.Supply <= 60
                        ? fleet.BaseSupplyWorld == fleet.World ? "Пополнение на договорной базе" : fleet.BaseSupplyWorld != null
                            ? "Комплект зарезервирован на другой базе" : "Договорная база: ожидание доставленных припасов"
                        : "На позиции" });
            if (!fleet.Alive) continue;
            foreach (var leader in _fleets.Values.Where(f => f.Id != fleet.Id && f.Alive))
            {
                var req = new SWEmpireMilitaryRequest { FleetId = fleet.Id, Action = SWEmpireMilitaryAction.Escort,
                    Target = $"fleet:{leader.Id}", RequestId = "preview" };
                TryQuote(req, out var q);
                state.Options.Add(new SWEmpireMilitaryOptionView { FleetId = fleet.Id, Action = req.Action, Target = req.Target,
                    Available = q.Allowed, Reason = q.Reason, Risk = "Сопровождающее соединение может понести потери" });
            }
            if (Republic(fleet.World))
            foreach (var sector in _worlds.Keys.Where(Republic).Select(PublicWorld).Distinct())
            {
                var req = new SWEmpireMilitaryRequest { FleetId = fleet.Id, Action = SWEmpireMilitaryAction.Raid, Target = sector, RequestId = "preview" };
                TryQuote(req, out var q);
                state.Options.Add(new SWEmpireMilitaryOptionView { FleetId = fleet.Id, Action = req.Action, Target = sector,
                    Available = q.Allowed, Reason = q.Reason, Risk = "Снабжение ограничено; возможен вынужденный отход" });
            }
            foreach (var action in new[] { SWEmpireMilitaryAction.Return, SWEmpireMilitaryAction.Repair })
            {
                var req = new SWEmpireMilitaryRequest { FleetId = fleet.Id, Action = action, Target = fleet.World, RequestId = "preview" };
                TryQuote(req, out var q);
                state.Options.Add(new SWEmpireMilitaryOptionView { FleetId = fleet.Id, Action = action, Target = PublicWorld(fleet.World),
                    Available = q.Allowed, Reason = q.Reason, Cost = q.Cost, EtaHours = q.EtaHours });
            }
            foreach (var target in _links.Where(l => l.From == fleet.World && Republic(l.To) || l.To == fleet.World && Republic(l.From))
                         .Select(l => PublicWorld(l.From == fleet.World ? l.To : l.From)).Distinct())
            foreach (var action in new[] { SWEmpireMilitaryAction.BorderConflict, SWEmpireMilitaryAction.Raid, SWEmpireMilitaryAction.Blockade })
            {
                var req = new SWEmpireMilitaryRequest { FleetId = fleet.Id, Action = action, Target = target, RequestId = "preview" };
                TryQuote(req, out var q);
                state.Options.Add(new SWEmpireMilitaryOptionView { FleetId = fleet.Id, Action = action, Target = target,
                    Available = q.Allowed, Reason = q.Reason, Risk = "Возможны потери и вынужденный отход", Cost = q.Cost, EtaHours = q.EtaHours });
            }
            foreach (var world in _worlds.Keys.Where(Imperial))
            foreach (var action in new[] { SWEmpireMilitaryAction.Move, SWEmpireMilitaryAction.Guard, SWEmpireMilitaryAction.Patrol, SWEmpireMilitaryAction.Recon, SWEmpireMilitaryAction.Train })
            {
                var req = new SWEmpireMilitaryRequest { FleetId = fleet.Id, Action = action, Target = world, RequestId = "preview" };
                TryQuote(req, out var q);
                state.Options.Add(new SWEmpireMilitaryOptionView { FleetId = fleet.Id, Action = action, Target = world,
                    Available = q.Allowed, Reason = q.Reason, Cost = q.Cost, EtaHours = q.EtaHours });
            }
            foreach (var world in _worlds.Keys.Where(w => !Imperial(w) && _diplomacy.AllowsCivilianBasing(w)))
            {
                var req = new SWEmpireMilitaryRequest { FleetId = fleet.Id, Action = SWEmpireMilitaryAction.Move, Target = world, RequestId = "preview" };
                TryQuote(req, out var q);
                state.Options.Add(new SWEmpireMilitaryOptionView { FleetId = fleet.Id, Action = req.Action, Target = world,
                    Available = q.Allowed, Reason = q.Reason, EtaHours = q.EtaHours });
            }
        }
        foreach (var world in _worlds.Keys.Where(Yard))
        {
            var req = new SWEmpireMilitaryRequest { Action = SWEmpireMilitaryAction.Recruit, Target = world, RequestId = "preview" };
            TryQuote(req, out var q);
            state.Options.Add(new SWEmpireMilitaryOptionView { Action = req.Action, Target = world,
                Available = q.Allowed, Reason = q.Reason, Cost = q.Cost, EtaHours = q.EtaHours });
        }
        return state;
    }
}
