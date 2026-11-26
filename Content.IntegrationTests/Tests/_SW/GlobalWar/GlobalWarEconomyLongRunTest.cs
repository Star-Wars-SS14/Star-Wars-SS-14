using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Content.Server._SW.GlobalWar.Market;
using Content.Server._SW.GlobalWar.Politics;
using Content.Server.GameTicking;
using Content.Shared._SW.GlobalWar;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

/// <summary>
/// Calculation scenarios load the real campaign manifest. Their controlled settlement/policy adapters are explicit:
/// real ledger, C command transitions, physical production and network DTO costs require separate integration verification.
/// </summary>
[TestFixture, NonParallelizable, Explicit("Takes up to 20 minutes, near the pool watchdog. Run by hand after economy changes.")]
public sealed class GlobalWarEconomyLongRunTest
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task All22MarketsConserveMaterialsFor180MinutesAcrossTimeAndInputOrders(bool demography)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        SWGlobalWarCommodityDefinition[] commodities = [];
        SWGlobalWarMarketDefinition[] definitions = [];
        SWGlobalWarTradeConnection[] connections = [];
        SWGlobalWarEconomyFlowPrototype[] flows = [];
        var taxes = new Dictionary<string, int>();
        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<GameTicker>().StartGameRule("SWGlobalWarCampaign"), Is.True);
            var model = server.System<SWGlobalWarMarketSystem>().Simulation!;
            var prototypes = server.ResolveDependency<IPrototypeManager>();
            Assert.That(model.Time, Is.EqualTo(TimeSpan.Zero));
            var expectedPlanets = new[] { "R", "E" }.SelectMany(side => Enumerable.Range(0, 10)
                .Select(index => $"SWGlobalWar{side}{index}")).Concat(["SWGlobalWarN0", "SWGlobalWarN1"]);
            Assert.That(model.Markets.Keys, Is.EquivalentTo(expectedPlanets));
            Assert.That(model.Markets.Values.All(m => m.Profile != null), Is.True);
            Assert.That(model.Flows, Has.Count.EqualTo(163));
            Assert.That(model.Connections, Has.Count.EqualTo(174));
            definitions = model.Markets.Values.Select(m => new SWGlobalWarMarketDefinition(m.Planet,
                m.Goods.Select(g => new SWGlobalWarMarketGoodDefinition(g.Key, g.Value.Stock,
                    g.Value.BaseConsumptionPerMinute)).ToArray(), m.Owner, m.Profile)).ToArray();
            commodities = definitions.SelectMany(d => d.Goods).Select(g => g.Commodity).Distinct().Select(id =>
            {
                var prototype = prototypes.Index<SWGlobalWarCommodityPrototype>(id);
                taxes[id] = prototype.ConsumptionTax;
                return new SWGlobalWarCommodityDefinition(id, prototype.CargoVolume);
            }).ToArray();
            connections = model.Connections.Values.ToArray();
            flows = prototypes.EnumeratePrototypes<SWGlobalWarEconomyFlowPrototype>().Where(f => !pair.IsTestPrototype(f)).ToArray();
        });
        await pair.CleanReturnAsync();

        var opening = commodities.ToDictionary(c => c.Id,
            c => definitions.Sum(d => (long) d.Goods.Single(g => g.Commodity == c.Id).Stock));
        var reference = Create(false);
        var partitioned = Create(true);
        var referenceTaxes = new Dictionary<SWGlobalWarFaction, long>();
        var partitionedTaxes = new Dictionary<SWGlobalWarFaction, long>();
        var verificationMilliseconds = 0.0;
        long verificationAllocations = 0;
        var durations = new List<double>();
        var grossDurations = new List<double>();
        var verificationDurations = new List<double>();
        var allocations = new List<long>();
        var grossAllocations = new List<long>();
        var balanceAllocations = new List<long>();
        var observations = new Dictionary<SWGlobalWarMarketSimulation, Observation>
        {
            [reference] = new(), [partitioned] = new(),
        };
        Attach(reference, referenceTaxes);
        Attach(partitioned, partitionedTaxes);
        var fractions = new[] { 0.25, 2.75, 0.5, 4.5, 0.125, 1.875 };
        var fractionIndex = 0;
        var industryHistory = new Dictionary<int, Dictionary<string, (long Stock, long Produced, long Received, long Sent, long Consumed)>>();
        var evidenceDirectory = Environment.GetEnvironmentVariable("SW_GLOBAL_WAR_ECONOMY_EVIDENCE") ??
            Path.Combine(TestContext.CurrentContext.WorkDirectory, "TestResults", "_SW");
        Directory.CreateDirectory(evidenceDirectory);
        var evidencePath = Path.Combine(evidenceDirectory, $"economy-{demography}-{Guid.NewGuid():N}.jsonl");
        File.WriteAllText(evidencePath, string.Empty);
        TestContext.AddTestAttachment(evidencePath, "Economic checkpoints; quantities in integer quanta (60000/unit).");
        Sample(0);
        var checkpoints = demography ? new[] { 30, 60, 90, 120, 180 }
            : new[] { 20, 30, 60, 90, 119, 120, 121, 122, 123, 124, 125, 126, 127, 128, 130, 150, 180 };
        foreach (var minute in checkpoints)
        {
            var until = TimeSpan.FromMinutes(minute);
            while (reference.Time < until)
                AdvanceReference();
            var crossesBoundary = minute is 30 or 60 or 90 or 120 or 125;
            var partitionUntil = crossesBoundary ? until - TimeSpan.FromSeconds(1) : until;
            var supplied = partitioned.Time;
            while (supplied < partitionUntil)
            {
                var delta = TimeSpan.FromSeconds(fractions[fractionIndex++ % fractions.Length]);
                if (supplied + delta > partitionUntil)
                    delta = partitionUntil - supplied;
                partitioned.Advance(delta);
                supplied += delta;
            }
            Balance(reference);
            Balance(partitioned);
            Sample(minute);
            durations.Clear();
            grossDurations.Clear();
            verificationDurations.Clear();
            allocations.Clear();
            grossAllocations.Clear();
            balanceAllocations.Clear();
            if (crossesBoundary)
            {
                // One call straddles the boundary; the reference still advances only one second per call.
                AdvanceReference();
                partitioned.Advance(TimeSpan.FromSeconds(2));
            }
            Balance(reference);
            Balance(partitioned);
            Assert.That(Snapshot(partitioned), Is.EqualTo(Snapshot(reference)), $"partition at {reference.Time}");
            Assert.That(partitionedTaxes, Is.EquivalentTo(referenceTaxes));
        }
        foreach (var model in new[] { reference, partitioned })
        {
            Assert.That(model.Time, Is.EqualTo(TimeSpan.FromMinutes(180)));
            Assert.That(model.ConsumptionReceiptCount, Is.EqualTo(180 * 60));
            Assert.That(model.FailedConsumptionReceiptCount, Is.Zero);
            Assert.That(model.InternalReservationCount, Is.Zero);
            Assert.That(model.ExternalReservationCount, Is.Zero);
            Assert.That(model.Flows.Values.Where(f => f.Source == "SWGlobalWarN1" && f.Commodity == "SWGlobalWarE")
                .Sum(f => f.Dispatched), Is.GreaterThan(0), "Neutral electronics must participate in actual supply.");
            foreach (var industry in new[] { "SWGlobalWarR7", "SWGlobalWarR8", "SWGlobalWarE7", "SWGlobalWarE8" })
                Assert.That(model.Flows.Values.Where(f => f.Source == "SWGlobalWarN1" && f.Destination == industry &&
                    f.Commodity == "SWGlobalWarE").Sum(f => f.Received), Is.GreaterThan(0));
            if (!demography)
            {
                Assert.That(observations[model].N1DispatchDenied, Is.GreaterThan(0));
                Assert.That(observations[model].N1DispatchAllowedAfterRestore, Is.GreaterThan(0));
                continue;
            }
            var observation = observations[model];
            Assert.That(observation.DeniedDispatch, Is.GreaterThan(0));
            Assert.That(observation.AllowedDispatchAfterRestore, Is.GreaterThan(0));
            Assert.That(observation.FullMinute, Is.Not.Null);
            Assert.That(observation.ShortMinuteDuringBlockade, Is.Not.Null);
            Assert.That(observation.Recovered, Is.True);
        }
        if (demography)
        {
            Assert.That(reference.Markets.Values.Any(m => m.Goods["SWGlobalWarF"].UnmetConsumption > 0), Is.True);
            Assert.That(reference.Markets.Values.Any(m => m.Population > 1), Is.True);
        }
        else
        {
            Assert.That(reference.Markets.Values.All(m => m.Population == 1), Is.True);
            AssertIndustryInterval(20, 30, 0.225m);
            AssertIndustryInterval(60, 90, 0.475m);
            AssertIndustryInterval(122, 124, 0m);
            AssertIndustryInterval(150, 180, 0.475m);
        }

        void AssertIndustryInterval(int start, int end, decimal importRate)
        {
            foreach (var planet in new[] { "SWGlobalWarR7", "SWGlobalWarR8", "SWGlobalWarE7", "SWGlobalWarE8" })
            {
                var before = industryHistory[start][planet];
                var after = industryHistory[end][planet];
                var scale = (decimal) SWGlobalWarMarketUnits.PerUnit * (end - start);
                var received = (after.Received - before.Received) / scale;
                var produced = (after.Produced - before.Produced) / scale;
                var sent = (after.Sent - before.Sent) / scale;
                var stockChange = (after.Stock - before.Stock) / scale;
                var consumed = (after.Consumed - before.Consumed) / scale;
                var proof = JsonSerializer.Serialize(new
                {
                    RecordType = "industrial-interval", Planet = planet, StartMinute = start, EndMinute = end,
                    ExpectedImportPerMinute = importRate, ReceivedPerMinute = received, ProducedPerMinute = produced,
                    SentPerMinute = sent, ConsumedPerMinute = consumed, StockChangePerMinute = stockChange,
                    ExpectedProductionFromActualSupply = sent + consumed - received + stockChange,
                });
                File.AppendAllText(evidencePath, proof + Environment.NewLine);
                TestContext.Out.WriteLine(proof);
                // At most one quantum per fixed second of rate rounding: 60/60000 units per minute.
                const decimal roundingPerMinute = 60m / SWGlobalWarMarketUnits.PerUnit;
                Assert.That(received, Is.EqualTo(importRate).Within(roundingPerMinute), proof);
                Assert.That(produced, Is.EqualTo(sent + consumed - received + stockChange).Within(roundingPerMinute), proof);
            }
        }

        void AdvanceReference()
        {
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            verificationMilliseconds = 0;
            verificationAllocations = 0;
            var started = Stopwatch.GetTimestamp();
            reference.Advance(TimeSpan.FromSeconds(1));
            var gross = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var grossBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
            durations.Add(Math.Max(0, gross - verificationMilliseconds));
            grossDurations.Add(gross);
            verificationDurations.Add(verificationMilliseconds);
            allocations.Add(grossBytes - verificationAllocations);
            grossAllocations.Add(grossBytes);
            balanceAllocations.Add(verificationAllocations);
        }

        SWGlobalWarMarketSimulation Create(bool reverse)
        {
            var model = new SWGlobalWarMarketSimulation(reverse ? commodities.Reverse() : commodities,
                reverse ? definitions.Reverse() : definitions, reverse ? connections.Reverse() : connections, Guid.NewGuid())
            {
                DemographyEnabled = demography,
            };
            model.ConfigureFlows(reverse ? flows.Reverse() : flows);
            foreach (var (commodity, rate) in taxes)
                model.TaxRates[commodity] = rate;
            return model;
        }

        void Attach(SWGlobalWarMarketSimulation model, Dictionary<SWGlobalWarFaction, long> receipts)
        {
            model.TradePolicy = request =>
            {
                var farmBlocked = demography && model.Time >= TimeSpan.FromMinutes(60) &&
                    model.Time < TimeSpan.FromMinutes(90) &&
                    (request.Sender.Id.EndsWith("R1") || request.Sender.Id.EndsWith("R2") ||
                     request.Sender.Id.EndsWith("E1") || request.Sender.Id.EndsWith("E2"));
                var neutralExport = request.Sender.Id == "SWGlobalWarN1" && request.Commodity.Id == "SWGlobalWarE";
                var neutralBlocked = !demography && neutralExport && request.Stage == SWGlobalWarTradeStage.Dispatch &&
                    model.Time >= TimeSpan.FromMinutes(120) && model.Time < TimeSpan.FromMinutes(125);
                var blocked = farmBlocked || neutralBlocked;
                if (neutralBlocked)
                    observations[model].N1DispatchDenied++;
                else if (!demography && neutralExport && request.Stage == SWGlobalWarTradeStage.Dispatch &&
                         model.Time >= TimeSpan.FromMinutes(125))
                    observations[model].N1DispatchAllowedAfterRestore++;
                if (request.Stage == SWGlobalWarTradeStage.Dispatch)
                {
                    if (blocked)
                        observations[model].DeniedDispatch++;
                    else if (model.Time >= TimeSpan.FromMinutes(90))
                        observations[model].AllowedDispatchAfterRestore++;
                }
                return new SWGlobalWarTradeDecision(!blocked, blocked ? "scenario-blockade" : string.Empty,
                    0, 0, 0, 0, 0);
            };
            model.SettleConsumption = (plan, complete) =>
            {
                foreach (var line in plan.Lines)
                    receipts[line.TaxRecipient] = receipts.GetValueOrDefault(line.TaxRecipient) +
                        (long) line.Quantity * line.TaxRate * line.TaxMultiplier;
                complete(true);
                return true;
            };
            // Before a step this verifies its completed predecessor; after consumption it also checks the intermediate
            // closed goods settlement. The final completed step is explicitly checked after each Advance window.
            model.IsActive = () =>
            {
                var started = Stopwatch.GetTimestamp();
                var allocated = GC.GetAllocatedBytesForCurrentThread();
                Balance(model);
                verificationMilliseconds += Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                verificationAllocations += GC.GetAllocatedBytesForCurrentThread() - allocated;
                return true;
            };
        }

        void Balance(SWGlobalWarMarketSimulation model)
        {
            if (model.InternalReservationCount != 0)
                Assert.Fail($"Internal reservations survived settlement at {model.Time}.");
            var cargo = new Dictionary<(string Planet, string Commodity), long>();
            foreach (var shipment in model.Shipments)
            {
                var key = (shipment.Destination, shipment.Commodity);
                cargo[key] = cargo.GetValueOrDefault(key) + shipment.Quantity;
                if (shipment.Quantity <= 0 || shipment.Batch?.Quantity != shipment.Quantity)
                    Assert.Fail($"Invalid cargo at {model.Time}: {shipment.Commodity}");
            }
            var balance = commodities.ToDictionary(c => c.Id, c => -opening[c.Id]);
            foreach (var market in model.Markets.Values)
            {
                if (market.Population < 0.5m || market.Population > 1.5m)
                    Assert.Fail($"Population outside bounds: {market.Planet}/{model.Time}");
                foreach (var (commodity, good) in market.Goods)
                {
                    var stock = good.Stock;
                    if (good.MutableBatches!.Values.Any(b => b.Quantity <= 0 || b.Reserved < 0 || b.Reserved > b.Quantity))
                        Assert.Fail($"Invalid batch at {model.Time}: {market.Planet}/{commodity}");
                    if (stock < 0 || good.Reserved != 0 || stock + good.Incoming + good.ReservedIncoming > good.Capacity ||
                        good.Incoming != cargo.GetValueOrDefault((market.Planet, commodity)))
                        Assert.Fail($"Inventory invariant at {model.Time}: {market.Planet}/{commodity}");
                    balance[commodity] += stock + cargo.GetValueOrDefault((market.Planet, commodity)) +
                        good.Consumed + good.GrowthConsumed + good.RecipeConsumed - good.Produced;
                    var expectedInput = market.Profile!.Recipes.Sum(r =>
                        market.Goods[r.Output.Id].Produced * r.Inputs.GetValueOrDefault(commodity));
                    if (good.RecipeConsumed != expectedInput)
                        Assert.Fail($"Recipe vector at {model.Time}: {market.Planet}/{commodity}");
                }
            }
            foreach (var (commodity, difference) in balance)
                if (difference != 0)
                    Assert.Fail($"Material balance at {model.Time}: {commodity} difference={difference}");
            if (!demography || model.Time.Ticks % TimeSpan.TicksPerMinute != 0 || model.Time == TimeSpan.Zero)
                return;
            // Supply and Ln steps added an IsActive call before this minute's food; population moves only after it.
            if (model.ConsumptionReceiptCount != (int) model.Time.TotalSeconds)
                return;
            var observation = observations[model];
            if (observation.LastMinute == model.Time)
                return;
            var receiver = model.Markets["SWGlobalWarR0"];
            var food = receiver.Goods["SWGlobalWarF"];
            var consumed = food.Consumed - observation.Consumed;
            var unmet = food.UnmetConsumption - observation.Unmet;
            var growth = food.GrowthConsumed - observation.Growth;
            var required = consumed + unmet;
            var satisfaction = (decimal) consumed / required;
            var extra = Math.Clamp(4m * growth / required, 0, 1);
            var oldPopulation = observation.Population;
            var expectedPopulation = Math.Clamp(oldPopulation + (unmet == 0
                ? 0.02m * Math.Max(0, 1 - oldPopulation) + 0.02m * extra * (1 - oldPopulation / 1.5m)
                : -0.02m * (1 - satisfaction) * oldPopulation), 0.5m, 1.5m);
            Assert.That(receiver.Population, Is.EqualTo(expectedPopulation), $"actual-food population at {model.Time}");
            var witness = $"{model.Time}: P={oldPopulation}; required={required}; consumed={consumed}; " +
                $"growth={growth}; nextP={receiver.Population}";
            if (unmet == 0)
            {
                observation.FullMinute ??= witness;
                if (model.Time > TimeSpan.FromMinutes(90) && observation.ShortMinuteDuringBlockade != null)
                    observation.Recovered = true;
            }
            else if (model.Time > TimeSpan.FromMinutes(60) && model.Time <= TimeSpan.FromMinutes(90))
                observation.ShortMinuteDuringBlockade ??= witness;
            observation.LastMinute = model.Time;
            observation.Population = receiver.Population;
            observation.Consumed = food.Consumed;
            observation.Unmet = food.UnmetConsumption;
            observation.Growth = food.GrowthConsumed;
        }

        void Sample(int minute)
        {
            var sorted = durations.Order().ToArray();
            double Percentile(double fraction) => sorted.Length == 0 ? 0 : sorted[(int) ((sorted.Length - 1) * fraction)];
            industryHistory[minute] = reference.Markets.Values.Where(m => m.Planet.EndsWith("7") || m.Planet.EndsWith("8"))
                .ToDictionary(m => m.Planet, m =>
                {
                    var good = m.Goods["SWGlobalWarE"];
                    return (Stock: (long) good.Stock, Produced: good.Produced,
                        Received: reference.Flows.Values.Where(f => f.Destination == m.Planet && f.Commodity == "SWGlobalWarE")
                            .Sum(f => f.Received),
                        Sent: reference.Flows.Values.Where(f => f.Source == m.Planet && f.Commodity == "SWGlobalWarE")
                            .Sum(f => f.Dispatched), Consumed: good.Consumed);
                });
            var sample = JsonSerializer.Serialize(new
            {
                Scenario = demography ? "demography-blockade-60-90" : "fixed-P1",
                Minute = minute, Markets = reference.Markets.Count,
                StepsInWindow = durations.Count,
                Batches = reference.Markets.Values.Sum(m => m.Goods.Values.Sum(g => g.Batches.Count)),
                InTransit = reference.Shipments.Count(s => s.ArrivesAt != null),
                AtNode = reference.Shipments.Count(s => s.ArrivesAt == null),
                Held = reference.Shipments.Count(s => s.HoldReason.Length > 0),
                InternalReservations = reference.InternalReservationCount,
                InternalPending = reference.InternalPendingReservationCount,
                ExternalReservations = reference.ExternalReservationCount,
                ExternalPending = reference.ExternalPendingReservationCount,
                ExternalCompleted = reference.ExternalReservationCount - reference.ExternalPendingReservationCount,
                ConsumptionReceipts = reference.ConsumptionReceiptCount,
                ProcessManagedBytes = GC.GetTotalMemory(false),
                MemoryScope = "Managed process heap: both calculation models plus live/collectible server-pair objects",
                SimulationAllocatedBytes = allocations.Sum(),
                EstimatedSimulationBytesPerStep = allocations.Count == 0 ? 0 : allocations.Average(),
                GrossAllocatedBytes = grossAllocations.Sum(), VerificationAllocatedBytes = balanceAllocations.Sum(),
                GrossMilliseconds = grossDurations.Sum(), VerificationMilliseconds = verificationDurations.Sum(),
                TimingScope = "Estimate with controlled adapters; verification still affects GC and caches",
                GcScope = "Cumulative process-wide collection counts",
                MedianStepMs = Percentile(0.5), P95StepMs = Percentile(0.95),
                MaxStepMs = sorted.LastOrDefault(),
                Gc0 = GC.CollectionCount(0), Gc1 = GC.CollectionCount(1), Gc2 = GC.CollectionCount(2),
                PopulationMinimum = reference.Markets.Values.Min(m => m.Population),
                PopulationMaximum = reference.Markets.Values.Max(m => m.Population),
                UnmetFood = reference.Markets.Values.Sum(m => m.Goods["SWGlobalWarF"].UnmetConsumption),
                Witnesses = observations[reference],
                GoldFlows = reference.Flows.Values.Where(f => f.Commodity == "SWGlobalWarAu")
                    .ToDictionary(f => f.Id, f => new { f.Source, f.Destination, f.PerMinute, f.WaitReason, f.Dispatched }),
                Industry = reference.Markets.Values.Where(m => m.Planet.EndsWith("N1") || m.Planet.EndsWith("7") ||
                    m.Planet.EndsWith("8") || m.Planet.EndsWith("5")).ToDictionary(m => m.Planet, m =>
                    m.Goods.Where(g => g.Key is "SWGlobalWarE" or "SWGlobalWarS" or "SWGlobalWarFe" or
                        "SWGlobalWarC" or "SWGlobalWarAu" or "SWGlobalWarLn").ToDictionary(g => g.Key, g => new
                    {
                        g.Value.Stock, g.Value.Produced, g.Value.RecipeConsumed, g.Value.Consumed, g.Value.Incoming,
                        Order = m.ProductionOrders.GetValueOrDefault(g.Key),
                        Received = reference.Flows.Values.Where(f => f.Destination == m.Planet && f.Commodity == g.Key)
                            .Sum(f => f.Received),
                        Dispatched = reference.Flows.Values.Where(f => f.Source == m.Planet && f.Commodity == g.Key)
                            .Sum(f => f.Dispatched),
                    })),
            });
            File.AppendAllText(evidencePath, sample + Environment.NewLine);
            TestContext.Out.WriteLine(sample);
        }
    }

    private static string Snapshot(SWGlobalWarMarketSimulation model)
    {
        return model.Time + ":" + string.Join(';', model.Markets.Values.SelectMany(m => m.Goods.Select(g =>
            $"{m.Planet}/{g.Key}:{m.Population}/{g.Value.Stock}/{g.Value.Incoming}/{g.Value.Consumed}/" +
            $"{g.Value.UnmetConsumption}/{g.Value.GrowthConsumed}/{g.Value.RecipeConsumed}/{g.Value.Produced}/" +
            $"{g.Value.CampaignConsumptionProgress}/{g.Value.GrowthProgress}/{m.SendBudget}/{m.ReceiveBudget}/" +
            $"{m.ProductionOrders.GetValueOrDefault(g.Key)}/{m.ProductionProgress.GetValueOrDefault(g.Key)}/" +
            string.Join(',', g.Value.MutableBatches!.Values.Select(b =>
                $"{b.Origin}/{b.Owner}/{b.Quantity}/{b.Reserved}").Order())))) +
            ":" + string.Join(';', model.Shipments.Select(s =>
                $"{s.Source}/{s.Destination}/{s.Commodity}/{s.Quantity}/{s.Leg}/{s.DepartedAt}/{s.ArrivesAt}/" +
                $"{s.ReceivedAtNode}/{s.HoldReason}/{s.Batch!.Origin}/{s.Batch.Owner}/{s.Flow}/{string.Join(',', s.Route)}").Order()) +
            ":" + string.Join(';', model.Flows.Values.Select(f =>
                $"{f.Id}/{f.PerMinute}/{f.Dispatched}/{f.Received}/{f.DispatchBudget}/{f.WaitReason}"));
    }

    private sealed class Observation
    {
        public TimeSpan LastMinute { get; set; }
        public decimal Population { get; set; } = 1;
        public long Consumed { get; set; }
        public long Growth { get; set; }
        public long Unmet { get; set; }
        public int DeniedDispatch { get; set; }
        public int AllowedDispatchAfterRestore { get; set; }
        public int N1DispatchDenied { get; set; }
        public int N1DispatchAllowedAfterRestore { get; set; }
        public string? FullMinute { get; set; }
        public string? ShortMinuteDuringBlockade { get; set; }
        public bool Recovered { get; set; }
    }
}
