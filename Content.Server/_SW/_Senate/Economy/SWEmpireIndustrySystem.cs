using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server._SW._Senate.Core;
using Content.Server._SW._Senate.Economy.Simulation;
using Content.Shared._SW._Senate.Economy;
using Content.Shared._SW._Senate.Empire;
using Content.Shared.GameTicking;
using Robust.Shared.Prototypes;

namespace Content.Server._SW._Senate.Economy;

/// <summary>Authoritative admission and publication for Imperial industry. The worker owns all physical goods.</summary>
public sealed class SWEmpireIndustrySystem : EntitySystem
{
    [Dependency] private readonly SWEmpireEconomySystem _economy = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    private readonly Dictionary<string, (SWEmpireIndustryRequest Request, SWEmpireIndustryQuote Quote, double Start, SWEmpireCouncilSphere Sphere)> _orders = new();
    private readonly Dictionary<string, double> _concentrationCooldown = new();
    private readonly HashSet<string> _acceptedCommands = new();
    private readonly HashSet<string> _rejected = new();

    public override void Initialize()
    {
        UpdatesAfter.Add(typeof(SWEmpireEconomySystem));
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => { _orders.Clear(); _concentrationCooldown.Clear(); _acceptedCommands.Clear(); _rejected.Clear(); });
    }

    public bool TryQuote(SWEmpireIndustryRequest request, out SWEmpireIndustryQuote quote) =>
        TryQuoteInternal(request, out quote, null);

    private bool TryQuoteInternal(SWEmpireIndustryRequest request, out SWEmpireIndustryQuote quote, string? ignorePending)
    {
        var result = new SWEmpireIndustryQuote();
        quote = result;
        bool Reject(string reason) { result.Reason = reason; return false; }
        if (request == null || !Enum.IsDefined(request.Action) || string.IsNullOrWhiteSpace(request.RequestId) || request.RequestId.Length > 100)
            return Reject("Некорректный идентификатор заявки");
        if (request.World == null || request.Recipe == null || request.Link == null || request.Source == null ||
            request.Target == null || request.Good == null || new[] { request.World, request.Recipe, request.Link, request.Source, request.Target, request.Good }.Any(x => x.Length > 128))
            return Reject("Некорректные параметры заявки");
        if (!double.IsFinite(request.Amount) || !double.IsFinite(request.DeadlineHours) || request.Amount < 0 || request.DeadlineHours < 0)
            return Reject("Количество и срок должны быть конечными и неотрицательными");
        if (!_time.ModeActive || !_time.DayActive || _economy.Runner is not { } runner || runner.Fault != null || runner.Snapshot is not { } snapshot)
            return Reject("Экономика недоступна вне активного дня");
        if (_orders.ContainsKey(request.RequestId) || _acceptedCommands.Contains(request.RequestId)) return Reject("Заявка с таким идентификатором уже существует");
        if (request.Action == SWEmpireIndustryAction.AcceptMaterial)
        {
            if (_rejected.Contains(request.World) || !_orders.TryGetValue(request.World, out var pending) || pending.Request.Action != SWEmpireIndustryAction.Ship ||
                !string.IsNullOrEmpty(pending.Request.Source) || snapshot.Hour >= pending.Start + pending.Request.DeadlineHours)
                return Reject("Заявка уже принята, отсутствует или срок истёк");
            var delivery = new SWEmpireIndustryRequest { Action = SWEmpireIndustryAction.Ship, RequestId = request.RequestId,
                Source = request.Source, Target = pending.Request.Target, Good = pending.Request.Good, Amount = pending.Request.Amount,
                DeadlineHours = pending.Start + pending.Request.DeadlineHours - snapshot.Hour, Urgent = false };
            if (!TryQuoteInternal(delivery, out var deliveryQuote, request.World)) { quote = deliveryQuote; return false; }
            quote = deliveryQuote;
            quote.Cost = 0;
            return true;
        }
        if (!double.IsFinite(request.Amount) || request.Amount <= 0 || request.Amount > 1000)
            return Reject("Количество должно быть конечным и положительным");
        var definition = runner.Definition;
        bool Active(string id) => definition.Worlds.Any(w => w.Id == id && w.Active);
        EconomyProduction? line = null;
        EconomyProduction? newLine = null;
        if (request.Action is SWEmpireIndustryAction.Ship)
        {
            if (!Active(request.Source) || !Active(request.Target) || request.Source == request.Target ||
                !definition.Goods.Any(g => g.Id == request.Good && g.CargoSlots > 0) ||
                !definition.Inventory.Any(i => i.World == request.Source && i.Good == request.Good) ||
                !definition.Inventory.Any(i => i.World == request.Target && i.Good == request.Good))
                return Reject("Источник, получатель или товар недоступны");
            if (!double.IsFinite(request.DeadlineHours) || request.DeadlineHours < 1 || request.DeadlineHours > 42)
                return Reject("Срок поставки должен быть от 1 до 42 часов");
            if (request.Urgent && (request.DeadlineHours > 8 || _orders.Values.Count(o => o.Request.Urgent && o.Request.Action == SWEmpireIndustryAction.Ship &&
                    !_rejected.Contains(o.Request.RequestId) && snapshot.Hour < o.Start + o.Request.DeadlineHours && !snapshot.Projects.Any(p => p.Id == o.Request.RequestId && p.CompletedHour != null)) >= 3))
                return Reject("Срочные перевозки ограничены тремя заявками и сроком до 8 часов");
            var target = snapshot.Inventory.FirstOrDefault(i => i.World == request.Target && i.Good == request.Good);
            var promised = PendingCapacity(snapshot, request.Target, request.Good, ignorePending);
            if (target == null || request.Amount > target.Capacity - target.Stock - target.Reserved - promised + 1e-9)
                return Reject("На складе получателя недостаточно места");
            var eta = ShortestPath(definition, request.Source, request.Target);
            if (eta == null) return Reject("Доступный имперский маршрут не найден");
            // This is only a lower bound: departure may wait for stock, fuel or capacity.
            quote.EtaHours = eta;
            quote.Reason = $"Минимальное время пути {eta:0.##} ч; отправка зависит от запасов и транспорта";
            quote.WorkHours = .25;
            quote.Cost = Math.Clamp((int) Math.Ceiling(50 + 10 * request.Amount), 50, 10000);
            quote.Materials.Add(new() { Good = request.Good, Quantity = request.Amount });
        }
        else if (request.Action == SWEmpireIndustryAction.ExpandRoute)
        {
            var edge = definition.Edges.FirstOrDefault(e => e.Id == request.Link && e.CapacitySlotsPerHour > 0);
            if (edge == null || !Active(request.World) || !Active(edge.A) || !Active(edge.B) || (edge.A != request.World && edge.B != request.World) ||
                request.Amount > edge.CapacitySlotsPerHour * .5)
                return Reject("Маршрут или прирост пропускной способности недоступен");
            quote.WorkHours = 4;
            quote.AddedRate = request.Amount;
            quote.Cost = 1000;
            quote.Materials.Add(new() { Good = "parts", Quantity = .5 });
            quote.Materials.Add(new() { Good = "circuits", Quantity = .25 });
        }
        else
        {
            if (!Active(request.World)) return Reject("Мир недоступен для имперской экономики");
            line = definition.Production.FirstOrDefault(p => p.World == request.World && p.Recipe == request.Recipe);
            if (request.Action == SWEmpireIndustryAction.CreateLine)
            {
                if (line != null || !_prototypes.TryIndex<SWPlanetEconomyPrototype>(request.World, out var world) ||
                    !_prototypes.TryIndex<SWProductionRecipePrototype>(request.Recipe, out var recipe) ||
                    !world.Facilities.Contains(recipe.Facility) || recipe.Requirements.Any(c => !world.Conditions.Contains(c)) || recipe.ResourceCosts.Count != 0 || request.Amount > 1)
                    return Reject("Рецепт не соответствует предприятиям и ресурсам мира");
                newLine = new EconomyProduction(request.World, recipe.ID, request.Amount, recipe.Hours, recipe.Power, recipe.Labor,
                    recipe.Inputs.Select(a => new EconomyAmount(a.Key.Id, a.Value)).ToArray(), recipe.Outputs.Select(a => new EconomyAmount(a.Key.Id, a.Value)).ToArray());
                if (newLine.Inputs.Concat(newLine.Outputs).Any(a => !definition.Inventory.Any(i => i.World == request.World && i.Good == a.Good)))
                    return Reject("Для одного из товаров нет складской ёмкости");
                line = newLine;
            }
            else if (line == null) return Reject("Производственная линия не найдена");
            if (request.Action == SWEmpireIndustryAction.UpgradeLine &&
                (request.Amount > line!.Rate || runner.IsExpansionPending(request.World, request.Recipe) ||
                 snapshot.Production.FirstOrDefault(p => p.World == request.World && p.Recipe == request.Recipe) is not { } state ||
                 state.CommittedExpansionCyclesPerHour + request.Amount > line.Rate + 1e-9))
                return Reject("Прирост превышает доступный предел или линия уже расширяется");
            if (request.Action == SWEmpireIndustryAction.Concentrate)
            {
                if (request.Amount > 1.5 || request.Amount <= 1 || !double.IsFinite(request.DeadlineHours) || request.DeadlineHours < 1 || request.DeadlineHours > 4 ||
                    _concentrationCooldown.GetValueOrDefault(request.World) > snapshot.Hour ||
                    definition.Production.Count(p => p.World == request.World && p.Rate > 0) < 2)
                    return Reject("Концентрация недоступна: проверьте срок, предел и паузу между приказами");
                quote.Cost = 150;
                quote.WorkHours = request.DeadlineHours;
                quote.AddedRate = line!.Rate * (request.Amount - 1);
                quote.Power = quote.AddedRate * line.Hours * line.Power;
                quote.Labor = quote.AddedRate * line.Hours * line.Labor;
                quote.Reason = "Другие линии мира временно работают с половинной мощностью; ресурсы и энергия общие";
            }
            else
            {
                quote.WorkHours = 4;
                quote.AddedRate = request.Amount;
                quote.Power = request.Amount * line!.Hours * line.Power;
                quote.Labor = request.Amount * line.Hours * line.Labor;
                var world = definition.Worlds.First(w => w.Id == request.World);
                var capacity = snapshot.Capacities.FirstOrDefault(c => c.World == request.World);
                var powerLimit = Math.Max(0, world.BaseloadPower + (capacity?.ReactorCapacityPower ?? world.ReactorCapacityPower)
                    - (capacity?.DomesticPower ?? world.DomesticPower));
                var laborLimit = capacity?.LaborCapacity ?? world.LaborCapacity;
                // Accepted projects are recorded immediately, before the worker publishes them.
                // Completed projects are excluded: their capacity is now part of normal operation.
                var pendingPower = 0d;
                var pendingLabor = 0d;
                foreach (var order in _orders.Values.Where(o => o.Request.World == request.World &&
                             o.Request.Action is SWEmpireIndustryAction.UpgradeLine or SWEmpireIndustryAction.CreateLine))
                {
                    if (_rejected.Contains(order.Request.RequestId) || snapshot.Projects.FirstOrDefault(p => p.Id == order.Request.RequestId)?.CompletedHour != null) continue;
                    pendingPower += order.Quote.Power;
                    pendingLabor += order.Quote.Labor;
                }
                var totalPower = quote.Power + pendingPower;
                var totalLabor = quote.Labor + pendingLabor;
                if (totalPower > powerLimit + 1e-9)
                    return Reject($"Предел энергии с учётом строящихся проектов: доступно {powerLimit:0.##}, требуется {totalPower:0.##}");
                if (totalLabor > laborLimit + 1e-9)
                    return Reject($"Предел труда с учётом строящихся проектов: доступно {laborLimit:0.##}, требуется {totalLabor:0.##}");

                // This is a construction quote, not a reservation of a specific simulation tick.
                // Running batches and fuel stock fluctuate; the worker still consumes actual fuel
                // and limits production when either resource is unavailable.
                var states = snapshot.Production.Where(p => p.World == request.World).ToDictionary(p => p.Recipe);
                var activeLines = definition.Production.Where(p => p.World == request.World && states.ContainsKey(p.Recipe));
                var usedPower = activeLines.Sum(p => states[p.Recipe].WorkInProgress * p.Power);
                var usedLabor = activeLines.Sum(p => states[p.Recipe].WorkInProgress * p.Labor);
                var fuel = snapshot.Inventory.FirstOrDefault(i => i.World == request.World && i.Good == "reactor_fuel")?.Stock ?? 0;
                var fueledReactor = Math.Min(capacity?.ReactorCapacityPower ?? world.ReactorCapacityPower,
                    Math.Max(0, fuel) * definition.ReactorPowerPerFuel / SWRepublicEconomyRunner.StepHours);
                var availablePower = Math.Max(0, world.BaseloadPower + fueledReactor - (capacity?.DomesticPower ?? world.DomesticPower) - usedPower);
                var availableLabor = Math.Max(0, laborLimit - usedLabor);
                var shortages = new List<string>();
                if (totalPower > availablePower + 1e-9)
                    shortages.Add($"энергия: доступно {availablePower:0.##}, требуется {totalPower:0.##}");
                if (totalLabor > availableLabor + 1e-9)
                    shortages.Add($"труд: доступно {availableLabor:0.##}, требуется {totalLabor:0.##}");
                if (shortages.Count > 0)
                    quote.Reason = $"Строительство доступно. Для полной загрузки сейчас не хватает: {string.Join("; ", shortages)}. Работа зависит от снабжения";
                quote.Cost = Math.Clamp((int) Math.Ceiling(650 + 250 * request.Amount), 650, 1600);
                quote.Materials.Add(new() { Good = "building_modules", Quantity = .25 });
                quote.Materials.Add(new() { Good = "machinery", Quantity = .15 });
                quote.Materials.Add(new() { Good = "parts", Quantity = .2 });
            }
        }
        quote.Allowed = true;
        return true;
    }

    public bool TrySubmit(SWEmpireIndustryRequest request, out string reason)
    {
        if (!TryQuote(request, out var quote)) { reason = quote.Reason; return false; }
        var runner = _economy.Runner!;
        var snapshot = runner.Snapshot!;
        if (request.Action == SWEmpireIndustryAction.AcceptMaterial)
        {
            var order = _orders[request.World];
            var delivery = order.Request;
            var remaining = order.Start + delivery.DeadlineHours - snapshot.Hour;
            var project = new EconomyProjectRequest(delivery.RequestId, delivery.Target,
                new[] { new EconomyAmount(delivery.Good, delivery.Amount) }, .25)
            { IndustryDelivery = new EconomyIndustryDelivery(request.Source, delivery.Urgent, remaining) };
            if (!runner.TryQueueProject(project)) { reason = "Очередь отклонила доставку"; return false; }
            delivery.Source = request.Source;
            _orders[request.World] = (delivery, order.Quote, order.Start, order.Sphere);
            _acceptedCommands.Add(request.RequestId);
            reason = string.Empty;
            return true;
        }
        if (request.Action == SWEmpireIndustryAction.Concentrate)
        {
            var mods = runner.Definition.Production.Where(p => p.World == request.World && p.Rate > 0)
                .Select(p => new EconomyProductionModifier(p.World, p.Recipe, p.Recipe == request.Recipe ? request.Amount : .5)).ToArray();
            if (!runner.SetTimedProductionModifiers("empire-industry:" + request.World, mods, snapshot.Hour + request.DeadlineHours))
            { reason = "Не удалось применить производственный приказ"; return false; }
            _concentrationCooldown[request.World] = snapshot.Hour + request.DeadlineHours + 4;
        }
        else
        {
            EconomyProduction? newLine = null;
            if (request.Action == SWEmpireIndustryAction.CreateLine)
            {
                var p = _prototypes.Index<SWProductionRecipePrototype>(request.Recipe);
                newLine = new EconomyProduction(request.World, p.ID, request.Amount, p.Hours, p.Power, p.Labor,
                    p.Inputs.Select(x => new EconomyAmount(x.Key.Id, x.Value)).ToArray(), p.Outputs.Select(x => new EconomyAmount(x.Key.Id, x.Value)).ToArray());
            }
            var materials = request.Action == SWEmpireIndustryAction.Ship
                ? new[] { new EconomyAmount(request.Good, request.Amount) }
                : quote.Materials.Select(m => new EconomyAmount(m.Good, m.Quantity)).ToArray();
            var project = new EconomyProjectRequest(request.RequestId,
                request.Action == SWEmpireIndustryAction.Ship ? request.Target : request.World, materials, quote.WorkHours,
                request.Action == SWEmpireIndustryAction.UpgradeLine ? new EconomyProductionExpansion(request.Recipe, request.Amount) : null)
            {
                TransitExpansion = request.Action == SWEmpireIndustryAction.ExpandRoute ? new EconomyTransitExpansion(request.Link, request.Amount) : null,
                IndustryDelivery = request.Action == SWEmpireIndustryAction.Ship ? new EconomyIndustryDelivery(request.Source, request.Urgent, request.DeadlineHours) : null,
                NewProduction = newLine,
            };
            if (!runner.TryQueueProject(project)) { reason = "Очередь отклонила проект"; return false; }
        }
        // Retain a detached copy; clients may reuse or mutate their request object.
        _orders.Add(request.RequestId, (new SWEmpireIndustryRequest { Action = request.Action, RequestId = request.RequestId, World = request.World,
            Recipe = request.Recipe, Link = request.Link, Source = request.Source, Target = request.Target, Good = request.Good,
            Amount = request.Amount, DeadlineHours = request.DeadlineHours, Urgent = request.Urgent }, quote, snapshot.Hour, SWEmpireCouncilSphere.ProductionLogistics));
        reason = string.Empty;
        return true;
    }

    public List<SWEmpireIndustryProjectView> BuildProjects()
    {
        var snapshot = _economy.Snapshot;
        return _orders.Values.Select(o =>
        {
            var p = snapshot?.Projects.FirstOrDefault(x => x.Id == o.Request.RequestId);
            var expired = snapshot != null && o.Request.DeadlineHours > 0 && snapshot.Hour >= o.Start + o.Request.DeadlineHours;
            return new SWEmpireIndustryProjectView { Id = o.Request.RequestId, Action = o.Request.Action, World = o.Request.World,
                Source = o.Request.Source, Target = o.Request.Target, Good = o.Request.Good, Recipe = o.Request.Recipe, Link = o.Request.Link,
                Amount = o.Request.Amount, Cost = o.Quote.Cost, WorkHours = o.Quote.WorkHours, AddedRate = o.Quote.AddedRate,
                WorkedHours = o.Request.Action == SWEmpireIndustryAction.Concentrate && snapshot != null
                    ? Math.Clamp(snapshot.Hour - o.Start, 0, o.Quote.WorkHours) : p?.WorkDone ?? 0,
                Urgent = o.Request.Urgent, DeadlineHours = o.Request.DeadlineHours, SourceSphere = o.Sphere,
                AwaitingSource = o.Request.Action == SWEmpireIndustryAction.Ship && string.IsNullOrEmpty(o.Request.Source) && !expired && !_rejected.Contains(o.Request.RequestId),
                ExpiredUnaccepted = o.Request.Action == SWEmpireIndustryAction.Ship && string.IsNullOrEmpty(o.Request.Source) && expired && !_rejected.Contains(o.Request.RequestId),
                RejectedUnaccepted = _rejected.Contains(o.Request.RequestId),
                Delivered = p?.DeliveredQuantity ?? 0,
                Sent = p?.SentQuantity ?? 0, InTransit = p?.InTransitQuantity ?? 0,
                EtaHours = p?.Materials.FirstOrDefault()?.NextDeliveryHours,
                Status = _rejected.Contains(o.Request.RequestId) ? "Отклонено" :
                    p?.CompletedHour != null || o.Request.Action == SWEmpireIndustryAction.Concentrate && expired ? "Завершено" :
                    expired && o.Request.Action == SWEmpireIndustryAction.Ship ? "Срок истёк" :
                    o.Request.Action == SWEmpireIndustryAction.Ship && string.IsNullOrEmpty(o.Request.Source) ? "Ожидает выбора источника" : p?.Status switch
                    {
                        EconomyProjectStatus.WaitingMaterials => "Ожидает материалы",
                        EconomyProjectStatus.Working => "Выполняется",
                        EconomyProjectStatus.Delivering => "Доставляется",
                        EconomyProjectStatus.Completed => "Завершено",
                        _ => "В очереди",
                    },
                Reason = p?.Reason switch
                {
                    "materials" => "Ожидаются материалы",
                    "transit" => "Груз в пути",
                    "no_route" => "Нет доступного маршрута",
                    "transport" => "Ожидается транспорт",
                    "storage" => "Склад получателя заполнен",
                    "deadline" => "Срок отправки истёк",
                    null => o.Quote.Reason,
                    "" => string.Empty,
                    _ => "Ожидаются ресурсы или транспорт",
                } };
        }).ToList();
    }

    private static SWEmpireIndustryRequest MaterialRequest(SWEmpireMaterialRequest request) => new()
    {
        Action = SWEmpireIndustryAction.Ship, RequestId = request.RequestId,
        Target = request.World, World = request.World, Good = request.Good, Amount = request.Amount,
        DeadlineHours = request.DeadlineHours, Urgent = request.Urgent,
    };

    public bool TryQuoteMaterial(SWEmpireCouncilSphere sphere, SWEmpireMaterialRequest request, out SWEmpireIndustryQuote quote)
    {
        if (!Enum.IsDefined(sphere) || sphere == SWEmpireCouncilSphere.None || request == null || request.World == null || request.Good == null ||
            request.World.Length > 128 || request.Good.Length > 128)
        { quote = new SWEmpireIndustryQuote { Reason = "Недопустимая сфера или заявка" }; return false; }
        quote = new SWEmpireIndustryQuote();
        if (!_time.ModeActive || !_time.DayActive || _economy.Runner is not { } runner || runner.Fault != null || runner.Snapshot is not { } snapshot ||
            string.IsNullOrWhiteSpace(request.RequestId) || request.RequestId.Length > 100 || _orders.ContainsKey(request.RequestId) || _acceptedCommands.Contains(request.RequestId) ||
            !double.IsFinite(request.Amount) || request.Amount <= 0 || request.Amount > 1000 || !double.IsFinite(request.DeadlineHours) ||
            request.DeadlineHours < 1 || request.DeadlineHours > 42 || request.Urgent && request.DeadlineHours > 8 ||
            !runner.Definition.Worlds.Any(w => w.Id == request.World && w.Active) ||
            !runner.Definition.Goods.Any(g => g.Id == request.Good && g.CargoSlots > 0))
        { quote.Reason = "Заявка или получатель недоступны"; return false; }
        var store = snapshot.Inventory.FirstOrDefault(i => i.World == request.World && i.Good == request.Good);
        var promised = PendingCapacity(snapshot, request.World, request.Good, null);
        if (store == null || request.Amount > store.Capacity - store.Stock - store.Reserved - promised + 1e-9)
        { quote.Reason = "На складе получателя недостаточно места"; return false; }
        if (request.Urgent && _orders.Values.Count(o => o.Request.Urgent && o.Request.Action == SWEmpireIndustryAction.Ship &&
                !_rejected.Contains(o.Request.RequestId) && snapshot.Hour < o.Start + o.Request.DeadlineHours && !snapshot.Projects.Any(p => p.Id == o.Request.RequestId && p.CompletedHour != null)) >= 3)
        { quote.Reason = "Одновременно допускаются не более трёх срочных заявок"; return false; }
        quote.Cost = Math.Clamp((int) Math.Ceiling(50 + 10 * request.Amount), 50, 10000);
        quote.Reason = "Источник выберет сфера производства и логистики";
        quote.Allowed = true;
        return true;
    }

    public bool TrySubmitMaterial(SWEmpireCouncilSphere sphere, SWEmpireMaterialRequest request, out string reason)
    {
        if (!TryQuoteMaterial(sphere, request, out var quote)) { reason = quote.Reason; return false; }
        _orders.Add(request.RequestId, (MaterialRequest(request), quote, _economy.Snapshot!.Hour, sphere));
        reason = string.Empty;
        return true;
    }

    public bool TryRejectMaterial(string id, out string reason)
    {
        if (string.IsNullOrWhiteSpace(id) || _rejected.Contains(id) || !_orders.TryGetValue(id, out var order) ||
            order.Request.Action != SWEmpireIndustryAction.Ship || !string.IsNullOrEmpty(order.Request.Source) ||
            _economy.Snapshot is not { } snapshot || snapshot.Hour >= order.Start + order.Request.DeadlineHours)
        { reason = "Отклонить можно только ожидающую заявку"; return false; }
        _rejected.Add(id);
        reason = string.Empty;
        return true;
    }

    private double PendingCapacity(EconomySnapshot snapshot, string world, string good, string? ignore)
    {
        return _orders.Where(o => o.Key != ignore && !_rejected.Contains(o.Key) && o.Value.Request.Action == SWEmpireIndustryAction.Ship &&
                o.Value.Request.Target == world && o.Value.Request.Good == good)
            .Sum(o =>
            {
                var project = snapshot.Projects.FirstOrDefault(p => p.Id == o.Key);
                var remaining = Math.Max(0, o.Value.Request.Amount - (project?.DeliveredQuantity ?? 0));
                // Physical in-flight cargo is already in Inventory.Reserved. Only the
                // still-unshipped promise needs this admission-only reservation.
                return snapshot.Hour < o.Value.Start + o.Value.Request.DeadlineHours
                    ? Math.Max(0, remaining - (project?.InTransitQuantity ?? 0)) : 0;
            });
    }

    private static double? ShortestPath(EconomyDefinition definition, string source, string target)
    {
        var distances = new Dictionary<string, double> { [source] = 0 };
        var visited = new HashSet<string>();
        while (true)
        {
            var next = distances.Where(p => !visited.Contains(p.Key)).OrderBy(p => p.Value).FirstOrDefault();
            if (next.Key == null) return null;
            if (next.Key == target) return next.Value;
            visited.Add(next.Key);
            foreach (var edge in definition.Edges.Where(e => e.CapacitySlotsPerHour > 0 && (e.A == next.Key || e.B == next.Key)))
            {
                var neighbour = edge.A == next.Key ? edge.B : edge.A;
                var time = next.Value + edge.TravelHours;
                if (!distances.TryGetValue(neighbour, out var previous) || time < previous) distances[neighbour] = time;
            }
        }
    }
}
