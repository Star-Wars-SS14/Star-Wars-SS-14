using System;
using System.IO;
using Stopwatch = System.Diagnostics.Stopwatch;
using System.Text.Json;
using System.Linq;
using System.Reflection;
using Content.Client._SW.GlobalWar.UI;
using Robust.Client.UserInterface.Controls;
using Content.Server._SW.GlobalWar;
using Content.Server._SW.GlobalWar.Market;
using Content.Server._SW.GlobalWar.Politics;
using Content.Server.Damage.Systems;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Server.Preferences.Managers;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Content.Shared.CCVar;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

public sealed partial class GlobalWarEconomyIntegrationTest
{
    [TestPrototypes]
    private const string ViewPrototypes = """
- type: swGlobalWarPlanet
  id: SWSpawnTestEconomyViewR
  name: R
  initialOwner: Republic
  kind: Capital
  surface: SWSpawnTestMap
  orbit: SWSpawnTestMap
  economyProfile: SWGlobalWarEconomyCapital
  tradeConnections:
    SWSpawnTestEconomyViewE: { travelTime: 60, capacity: 64 }

- type: swGlobalWarPlanet
  id: SWSpawnTestEconomyViewE
  name: E
  initialOwner: Empire
  kind: Capital
  surface: SWSpawnTestMap
  orbit: SWSpawnTestMap
  economyProfile: SWGlobalWarEconomyCapital
  tradeConnections:
    SWSpawnTestEconomyViewR: { travelTime: 60, capacity: 64 }

- type: swGlobalWarPlanet
  id: SWSpawnTestEconomyViewRFarm
  name: RFarm
  initialOwner: Republic
  kind: Agriculture
  surface: SWSpawnTestMap
  orbit: SWSpawnTestMap
  economyProfile: SWGlobalWarEconomyAgriculture
  tradeConnections:
    SWSpawnTestEconomyViewR: { travelTime: 60, capacity: 64 }

- type: swGlobalWarPlanet
  id: SWSpawnTestEconomyViewEFarm
  name: EFarm
  initialOwner: Empire
  kind: Agriculture
  surface: SWSpawnTestMap
  orbit: SWSpawnTestMap
  economyProfile: SWGlobalWarEconomyAgriculture
  tradeConnections:
    SWSpawnTestEconomyViewE: { travelTime: 60, capacity: 64 }

- type: swGlobalWarPlanet
  id: SWSpawnTestEconomyViewN
  name: N
  initialOwner: Neutral
  kind: Industry
  surface: SWSpawnTestMap
  orbit: SWSpawnTestMap
  economyProfile: SWGlobalWarEconomyNeutralIndustry
  tradeConnections:
    SWSpawnTestEconomyViewRFarm: { travelTime: 60, capacity: 64 }

- type: swGlobalWarEconomyFlow
  id: SWSpawnTestEconomyViewFoodR
  source: SWSpawnTestEconomyViewRFarm
  destination: SWSpawnTestEconomyViewR
  commodity: SWGlobalWarF
  rateMode: RecipientMandatory
  share: 1
  priority: DomesticCivilian

- type: swGlobalWarEconomyFlow
  id: SWSpawnTestEconomyViewFoodE
  source: SWSpawnTestEconomyViewEFarm
  destination: SWSpawnTestEconomyViewE
  commodity: SWGlobalWarF
  rateMode: RecipientMandatory
  share: 1
  priority: DomesticCivilian

- type: swGlobalWarEconomyFlow
  id: SWSpawnTestEconomyViewNeutralImport
  source: SWSpawnTestEconomyViewN
  destination: SWSpawnTestEconomyViewRFarm
  commodity: SWGlobalWarE
  rateMode: Fixed
  share: 0.1
  priority: NeutralIndustry

- type: entity
  parent: SWDiplomacyTestRule
  id: SWSpawnTestEconomyViewRule
  components:
  - type: SWGlobalWarGamerule
    planets: [SWSpawnTestEconomyViewR, SWSpawnTestEconomyViewE, SWSpawnTestEconomyViewRFarm, SWSpawnTestEconomyViewEFarm, SWSpawnTestEconomyViewN]

- type: gamePreset
  id: SWSpawnTestEconomyViewPreset
  name: sw-global-war-title
  description: sw-global-war-description
  rules: [SWSpawnTestEconomyViewRule]
""";

    [Test]
    public async Task AddressedMarketAndTravelOmitSecretsAndBoundCargoPages()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            DummyTicker = false, Connected = true, InLobby = true, Dirty = true,
        });
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var originalPreset = ticker.Preset;
        var registry = server.System<SWGlobalWarAssignmentsSystem>();
        var markets = server.System<SWGlobalWarMarketSystem>();
        var console = server.System<SWGlobalWarMarketConsoleSystem>();
        var travel = server.System<SWGlobalWarTravelSystem>();
        var clock = server.System<SWGlobalWarCampaignSystem>();
        var planets = server.System<SWGlobalWarPlanetSystem>();
        var steps = server.System<SWGlobalWarCampaignStepSystem>();
        var timing = server.ResolveDependency<IGameTiming>();
        SWGlobalWarMarketConsoleState allowedView = default!;
        SWGlobalWarMarketConsoleState foodView = default!;
        await server.AddDummySessions(2);
        var loaded = false;
        for (var attempt = 0; attempt < 100 && !loaded; attempt++)
        {
            await pair.RunTicksSync(1);
            await server.WaitPost(() => loaded = server.PlayerMan.Sessions.All(p =>
                server.ResolveDependency<UserDbDataManager>().IsLoadComplete(p) &&
                server.ResolveDependency<IServerPreferencesManager>().TryGetCachedPreferences(p.UserId, out _)));
        }
        Assert.That(loaded, Is.True);
        var sessions = server.PlayerMan.Sessions.ToArray();
        for (var i = 0; i < sessions.Length; i++)
        {
            var profile = (HumanoidCharacterProfile) server.ResolveDependency<IServerPreferencesManager>()
                .GetPreferences(sessions[i].UserId).SelectedCharacter;
            ProtoId<JobPrototype> job = i == 0 ? "SWPoliticsTestHead" :
                i == 1 ? "SWPoliticsTestCivilian" : "SWDiplomacyTestEHead";
            await pair.SetJobPriorities(sessions[i].UserId,
                profile.JobPriorities.Keys.Select(j => (j, JobPriority.Never)).Append((job, JobPriority.High)).ToArray());
        }
        await server.WaitAssertion(() =>
        {
            var savedTime = timing.TimeBase;
            var savedTick = timing.CurTick;
            try
            {
                server.CfgMan.SetCVar(CCVars.GameMap, "SWSpawnTestMap");
                ticker.SetGamePreset("SWSpawnTestEconomyViewPreset");
                ticker.ToggleReadyAll(true);
                ticker.StartRound();
                Assert.That(ticker.PlayersJoinedRoundNormally, Is.EqualTo(3));
                Assert.That(clock.TryGetCampaign(out var campaign), Is.True);
                Assert.That(registry.TryGetAssignment(sessions[0].UserId, out var head), Is.True);
                Assert.That(head.Role, Is.EqualTo(SWGlobalWarRole.Head));
                var actor = head.Body;
                var civilian = sessions[1].AttachedEntity!.Value;
                var imperial = sessions[2].AttachedEntity!.Value;
                foreach (var session in sessions)
                    server.System<GodmodeSystem>().EnableGodmode(session.AttachedEntity!.Value);
                var model = markets.Simulation!;
                for (var second = 1; second <= 80; second++)
                {
                    timing.CurTick += 1;
                    timing.TimeBase = (timing.TimeBase.Item1 + TimeSpan.FromSeconds(second) - clock.Elapsed(campaign),
                        timing.TimeBase.Item2);
                    markets.Update(0);
                }
                var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                var started = Stopwatch.GetTimestamp();
                var view = console.GetState(actor, actor);
                var projectionMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                var projectionBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
                Assert.That(view.Planets.Count, Is.EqualTo(5));
                Assert.That(view.Assignment, Is.EqualTo(head.Id));
                Assert.That(view.Generation, Is.EqualTo(campaign.Comp.Generation));
                var own = view.Planets.Single(p => p.Planet.Id == "SWSpawnTestEconomyViewR");
                Assert.That(own.Availability, Is.EqualTo(SWGlobalWarMarketAvailability.Ready));
                var food = own.Goods.Single(g => g.Commodity.Id == "SWGlobalWarF");
                Assert.That(food.Stock, Is.EqualTo(model.Markets[own.Planet.Id].Goods["SWGlobalWarF"].Stock / 60000.0));
                Assert.That(food.TargetStock, Is.EqualTo(240));
                Assert.That(own.Goods.Single(g => g.Commodity.Id == "SWGlobalWarE").ConsumptionPerMinute,
                    Is.EqualTo(model.Markets[own.Planet.Id].Goods["SWGlobalWarE"].ConsumptionPerMinute / 60000.0));
                Assert.That(own.Goods.Single(g => g.Commodity.Id == "SWGlobalWarE").ConsumptionPerMinute,
                    Is.InRange(0.4, 0.41), "The current fractional rate includes the first demographic minute.");
                foreach (var hidden in view.Planets.Where(p => p.Planet.Id is "SWSpawnTestEconomyViewE" or "SWSpawnTestEconomyViewEFarm" or "SWSpawnTestEconomyViewN"))
                {
                    Assert.That(hidden.Availability, Is.EqualTo(SWGlobalWarMarketAvailability.Unknown));
                    Assert.That(hidden.Goods, Is.Empty);
                    Assert.That(hidden.Population, Is.Null);
                    Assert.That(hidden.SendCapacity, Is.Null);
                }
                Assert.That(model.Shipments.Any(s => s.Batch!.Owner == SWGlobalWarFaction.Empire), Is.True);
                Assert.That(view.TotalShipments, Is.EqualTo(model.Shipments.Count(s => s.Batch!.Owner == SWGlobalWarFaction.Republic ||
                    model.Markets[s.Destination].Owner == SWGlobalWarFaction.Republic)));
                Assert.That(view.TotalShipments, Is.GreaterThan(40));
                Assert.That(view.Shipments.Count, Is.EqualTo(40));
                Assert.That(view.Shipments.All(s => s.Owner != SWGlobalWarFaction.Empire && s.Id != Guid.Empty), Is.True);
                var neutralCargo = console.GetState(actor, actor, new(Commodity: "SWGlobalWarE"));
                Assert.That(neutralCargo.Shipments, Is.Not.Empty);
                Assert.That(neutralCargo.Shipments.All(s => s.Owner == SWGlobalWarFaction.Neutral && s.Destination.Id == "SWSpawnTestEconomyViewRFarm"), Is.True);
                allowedView = view;
                foodView = console.GetState(actor, actor, new(Commodity: "SWGlobalWarF"));
                Assert.That(foodView.TotalShipments, Is.GreaterThan(40));
                var next = console.GetState(actor, actor, new(Page: 1));
                Assert.That(next.Shipments.Count, Is.InRange(1, 40));
                Assert.That(next.Shipments.Select(s => s.Id).Intersect(view.Shipments.Select(s => s.Id)), Is.Empty);
                Assert.That(view.FreightTotals.Sum(t => t.Moving + t.Waiting),
                    Is.EqualTo(model.Shipments.Where(s => s.Batch!.Owner == SWGlobalWarFaction.Republic ||
                        model.Markets[s.Destination].Owner == SWGlobalWarFaction.Republic)
                        .Sum(s => s.Quantity / 60000.0)).Within(1e-9));
                var foreignLane = view.Connections.Single(c => c.From.Id == "SWSpawnTestEconomyViewEFarm");
                Assert.That(foreignLane.InFlightVolume, Is.Zero);
                Assert.That(foreignLane.OccupancyComplete, Is.False);
                var filtered = console.GetState(actor, actor, new(Planet: "SWSpawnTestEconomyViewE", Page: int.MaxValue));
                Assert.That(filtered.Shipments, Is.Empty);
                Assert.That(filtered.TotalShipments, Is.Zero);
                Assert.That(filtered.FreightTotals, Is.Empty);
                Assert.That(filtered.Query.Page, Is.Zero);
                var travelView = travel.GetState(actor, actor);
                foreach (var destination in travelView.Destinations)
                {
                    var market = view.Planets.Single(p => p.Planet == destination.Id);
                    Assert.That(destination.MarketAvailability, Is.EqualTo(market.Availability));
                    Assert.That(destination.Market, Is.EqualTo(market.Goods));
                }
                AssertClosed(console.GetState(actor));
                AssertClosed(console.GetState(actor, civilian));
                Assert.That(travel.GetState(actor, civilian).Destinations.All(d => d.Market!.Count == 0), Is.True);
                using var serialized = new MemoryStream();
                server.ResolveDependency<IRobustSerializer>().Serialize(serialized, view);
                Assert.That(serialized.Length, Is.LessThan(64 * 1024), "Bounded five-world projection, not a 180-minute network claim.");
                if (Environment.GetEnvironmentVariable("SW_GLOBAL_WAR_VIEW_EVIDENCE") is { Length: > 0 } evidenceDirectory)
                {
                    Directory.CreateDirectory(evidenceDirectory);
                    var evidencePath = Path.Combine(evidenceDirectory, "addressed-view-" + Guid.NewGuid().ToString("N") + ".json");
                    File.WriteAllText(evidencePath, JsonSerializer.Serialize(new
                    {
                        Scenario = "Five live worlds, 80 seconds, head view",
                        SerializedStateBytes = serialized.Length, ProjectionMilliseconds = projectionMilliseconds,
                        ProjectionAllocatedBytes = projectionBytes, PageSize = view.Shipments.Count,
                        view.TotalShipments, WorldCount = view.Planets.Count,
                        Scope = "Direct server projection and Robust state serialization, excluding network envelope/compression and client rendering",
                    }));
                    TestContext.AddTestAttachment(evidencePath);
                }

                void Capture(string id, SWGlobalWarPlanetLocation location, SWGlobalWarFaction owner = SWGlobalWarFaction.Empire)
                {
                    timing.CurTick += 1;
                    Assert.That(planets.TryGetPlanet(id, out var planet), Is.True);
                    var state = server.EntMan.GetComponent<SWGlobalWarPlanetComponent>(planet!.Value);
                    Assert.That(steps.TryQueueOwnership(planet.Value, location, owner,
                        state.OwnershipRevision, campaign.Comp.Generation, Guid.NewGuid()), Is.True);
                    Assert.That(steps.CommitCampaignStep(campaign.Comp.Generation, timing.CurTick), Is.True);
                }
                var imperialFlight = model.Shipments.First(c => c.Batch!.Owner == SWGlobalWarFaction.Empire && c.ArrivesAt != null);
                Assert.That(imperialFlight.DeliveryParticipant, Is.EqualTo(SWGlobalWarFaction.Empire));
                Assert.That(console.GetState(actor, imperial, new(Planet: "SWSpawnTestEconomyViewE")).Shipments,
                    Is.Not.Empty);
                Capture("SWSpawnTestEconomyViewE", SWGlobalWarPlanetLocation.Surface, SWGlobalWarFaction.Republic);
                Assert.That(imperialFlight.Batch!.Owner, Is.EqualTo(SWGlobalWarFaction.Empire));
                var capturedDestination = console.GetState(actor, actor, new(Planet: "SWSpawnTestEconomyViewE"));
                Assert.That(capturedDestination.Shipments, Is.Empty);
                Assert.That(capturedDestination.TotalShipments, Is.Zero);
                Assert.That(capturedDestination.FreightTotals, Is.Empty);
                Assert.That(capturedDestination.Connections.Single(c => c.From.Id == "SWSpawnTestEconomyViewEFarm").InFlightVolume, Is.Zero);
                Assert.That(console.GetState(actor, imperial, new(Planet: "SWSpawnTestEconomyViewE")).Shipments,
                    Is.Not.Empty, "The old delivery participant and cargo owner retain their existing delivery.");
                Capture("SWSpawnTestEconomyViewR", SWGlobalWarPlanetLocation.Orbit);
                Assert.That(markets.GetMarketAvailability("SWSpawnTestEconomyViewR", actor), Is.EqualTo(SWGlobalWarMarketAvailability.Ready));
                var sentIds = view.Shipments.Select(s => s.Id).ToHashSet();
                Capture("SWSpawnTestEconomyViewRFarm", SWGlobalWarPlanetLocation.Surface);
                Assert.That(markets.GetMarketView("SWSpawnTestEconomyViewRFarm", actor), Is.Empty);
                Assert.That(console.GetState(actor, actor).Shipments.Select(s => s.Id).Intersect(sentIds), Is.Not.Empty);
                var knownNeutral = console.GetState(actor, actor, new(Planet: "SWSpawnTestEconomyViewRFarm", Commodity: "SWGlobalWarE"));
                Assert.That(knownNeutral.Shipments, Is.Not.Empty);
                Assert.That(knownNeutral.Shipments.All(c => c.Owner == SWGlobalWarFaction.Neutral), Is.True);
                var unknownNeutral = console.GetState(actor, imperial,
                    new(Planet: "SWSpawnTestEconomyViewRFarm", Commodity: "SWGlobalWarE"));
                Assert.That(unknownNeutral.Shipments, Is.Empty);
                Assert.That(unknownNeutral.FreightTotals, Is.Empty);
                Assert.That(unknownNeutral.Connections.Single(c => c.From.Id == "SWSpawnTestEconomyViewN").InFlightVolume, Is.Zero);
                for (var second = 81; second <= 141; second++)
                {
                    timing.CurTick += 1;
                    timing.TimeBase = (timing.TimeBase.Item1 + TimeSpan.FromSeconds(second) - clock.Elapsed(campaign),
                        timing.TimeBase.Item2);
                    markets.Update(0);
                }
                Assert.That(imperialFlight.ArrivesAt, Is.Null);
                Assert.That(imperialFlight.Route[imperialFlight.Leg], Is.EqualTo("SWSpawnTestEconomyViewE"));
                Capture("SWSpawnTestEconomyViewE", SWGlobalWarPlanetLocation.Surface);
                Capture("SWSpawnTestEconomyViewE", SWGlobalWarPlanetLocation.Surface, SWGlobalWarFaction.Republic);
                Assert.That(imperialFlight.Batch.Owner, Is.EqualTo(SWGlobalWarFaction.Republic));
                Assert.That(imperialFlight.DeliveryParticipant, Is.Null);
                var capturedCargo = console.GetState(actor, actor, new(Planet: "SWSpawnTestEconomyViewE"));
                Assert.That(capturedCargo.Shipments.Any(c => c.Id == imperialFlight.Id && c.Captured), Is.True);
                Assert.That(console.GetState(actor, imperial, new(Planet: "SWSpawnTestEconomyViewE")).Shipments
                    .Any(c => c.Id == imperialFlight.Id), Is.False);
                var returnRequest = new SWGlobalWarMarketReturnRequest(campaign.Comp.Generation, Guid.NewGuid(),
                    imperialFlight.Id, SWGlobalWarFaction.Republic, "SWSpawnTestEconomyViewR", imperialFlight.Quantity);
                Assert.That(markets.TryReturnShipment(returnRequest, out _), Is.True);
                Assert.That(imperialFlight.DeliveryParticipant, Is.EqualTo(SWGlobalWarFaction.Republic));
                Assert.That(console.GetState(actor, actor, new(Planet: "SWSpawnTestEconomyViewE")).Shipments
                    .Any(c => c.Id == imperialFlight.Id && c.Returning), Is.True);
                Assert.That(console.GetState(actor, imperial, new(Planet: "SWSpawnTestEconomyViewE")).Shipments
                    .Any(c => c.Id == imperialFlight.Id), Is.False);
                Assert.That(registry.TryExecute(sessions[0], new SWGlobalWarAppointmentCommand(campaign.Comp.Generation,
                    Guid.NewGuid(), SWGlobalWarAppointmentAction.Resign, head.Revision, head.User, head.Revision), out _), Is.True);
                AssertClosed(console.GetState(actor, actor));
                Assert.That(travel.GetState(actor, actor).Destinations.All(d => d.Market!.Count == 0), Is.True);
            }
            finally
            {
                timing.TimeBase = savedTime;
                timing.CurTick = savedTick;
                ticker.SetGamePreset(originalPreset);
            }
        });
        await pair.Client.WaitAssertion(() =>
        {
            using var menu = new SWGlobalWarMarketConsoleMenu();
            const BindingFlags fields = BindingFlags.NonPublic | BindingFlags.Instance;
            var commodities = (System.Collections.Generic.List<ProtoId<SWGlobalWarCommodityPrototype>>)
                typeof(SWGlobalWarMarketConsoleMenu).GetField("_commodities", fields)!.GetValue(menu)!;
            typeof(SWGlobalWarMarketConsoleMenu).GetField("_commodity", fields)!
                .SetValue(menu, (ProtoId<SWGlobalWarCommodityPrototype>?) "SWGlobalWarF");
            var picker = menu.FindControl<OptionButton>("CommodityFilter");
            picker.SelectId(commodities.IndexOf("SWGlobalWarF") + 1);
            menu.UpdateState(foodView);
            Assert.That(picker.SelectedId, Is.GreaterThan(0));
            Assert.That(allowedView.Shipments.Select(s => s.Commodity).Distinct().Count(), Is.GreaterThan(1));
            var nextAssignment = new SWGlobalWarMarketConsoleState(allowedView.Ready, allowedView.Time,
                allowedView.Planets, allowedView.Connections, allowedView.Shipments, allowedView.CurrentPlanet,
                allowedView.Generation, Guid.NewGuid(), allowedView.AssignmentRevision + 1, allowedView.Partial,
                allowedView.Query, allowedView.TotalShipments, allowedView.FreightTotals);
            menu.UpdateState(nextAssignment);
            Assert.That(picker.SelectedId, Is.Zero);
            Assert.That(typeof(SWGlobalWarMarketConsoleMenu).GetField("_commodity", fields)!.GetValue(menu), Is.Null);
            Assert.That(typeof(SWGlobalWarMapControl).GetField("_marketCommodity", fields)!
                .GetValue(menu.FindControl<SWGlobalWarMapControl>("Map")), Is.Null);
            Assert.That(menu.FindControl<BoxContainer>("ShipmentRows").ChildCount,
                Is.EqualTo(nextAssignment.Shipments.Count));
            Assert.That(menu.FindControl<SWGlobalWarConsoleButton>("NextPage").Disabled, Is.False);
        });
        await pair.CleanReturnAsync();

        void AssertClosed(SWGlobalWarMarketConsoleState view)
        {
            Assert.That(view.Planets, Has.Count.EqualTo(5));
            Assert.That(view.Planets.All(p => p.Goods.Count == 0 && p.Availability == SWGlobalWarMarketAvailability.Restricted), Is.True);
            Assert.That(view.Shipments, Is.Empty);
            Assert.That(view.FreightTotals, Is.Empty);
            Assert.That(view.Connections.All(c => c.InFlightVolume == 0 && !c.OccupancyComplete), Is.True);
        }
    }
}
