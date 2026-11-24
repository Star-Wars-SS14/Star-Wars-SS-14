using System;
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server._SW.GlobalWar;
using Content.Server._SW.GlobalWar.Politics;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Content.Shared.Shuttles.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

public sealed partial class GlobalWarDiplomacyTest
{
    private static async Task CheckShipGroups(TestPair pair, string scenario, Guid generation, EntityUid rule,
        EntityUid first, EntityUid firstSailor, EntityUid secondSailor, EntityUid rCapital, EntityUid eCapital,
        Func<int, SWGlobalWarPoliticalAction, bool> act)
    {
        var server = pair.Server;
        var planets = server.System<SWGlobalWarPlanetSystem>();
        var ownership = server.System<SWGlobalWarShipOwnershipSystem>();
        var politics = server.System<SWGlobalWarPoliticsSystem>();
        var transform = server.System<SharedTransformSystem>();
        var mapSystem = server.System<Robust.Server.GameObjects.MapSystem>();
        var exclusions = new List<EntityUid>();
        EntityUid second = default;
        EntityUid firstCargo = default;
        EntityUid secondCargo = default;
        EntityCoordinates firstPosition = default;
        SWGlobalWarShipWithdrawalOrder initial = default!;
        SWGlobalWarShipWithdrawalOrder replacement = default!;
        var independent = scenario == "withdraw-ship-independent";
        await server.WaitAssertion(() =>
        {
            foreach (var planet in server.EntMan.GetComponent<SWGlobalWarGameruleComponent>(rule).PlanetEntities.Values)
            {
                if (server.EntMan.GetComponent<SWGlobalWarPlanetComponent>(planet).OrbitOwner != SWGlobalWarFaction.Republic)
                    continue;
                Assert.That(planets.TryEnsureLocation(planet, SWGlobalWarPlanetLocation.Orbit, out var map, out _), Is.True);
                exclusions.Add(server.EntMan.SpawnEntity("TravelTestExclusion", new EntityCoordinates(map!.Value, default)));
            }
            firstCargo = server.EntMan.SpawnEntity("Pen", new EntityCoordinates(first, 1.5f, 1.5f));
            firstPosition = server.EntMan.GetComponent<TransformComponent>(first).Coordinates;
            if (independent)
            {
                Assert.That(planets.TryEnsureLocation(rCapital, SWGlobalWarPlanetLocation.Orbit, out var map, out _), Is.True);
                var grid = server.ResolveDependency<IMapManager>().CreateGridEntity(server.EntMan.GetComponent<TransformComponent>(map.Value).MapID);
                second = grid.Owner;
                var tiles = new List<(Vector2i, Tile)>();
                for (var x = 0; x < 5; x++)
                for (var y = 0; y < 5; y++)
                    tiles.Add((new Vector2i(x, y), new Tile(1)));
                mapSystem.SetTiles(second, grid.Comp, tiles);
                transform.SetLocalPosition(second, new System.Numerics.Vector2(1000, 1000));
                Assert.That(ownership.RegisterShip(second, generation, SWGlobalWarFaction.Empire), Is.True);
                transform.SetCoordinates(secondSailor, new EntityCoordinates(second, 2.5f, 2.5f));
                secondCargo = server.EntMan.SpawnEntity("Pen", new EntityCoordinates(second, 1.5f, 1.5f));
            }
            Assert.That(act(0, SWGlobalWarPoliticalAction.OfferTruce), Is.True);
            Assert.That(act(1, SWGlobalWarPoliticalAction.OfferTruce), Is.True);
            Assert.That(politics.TryGetPolitics(out var state), Is.True);
            initial = state.Comp.ShipWithdrawals[first];
            if (independent)
                Assert.That(state.Comp.ShipWithdrawals, Does.ContainKey(second));
        });
        await pair.RunSeconds(4);
        await server.WaitAssertion(() =>
        {
            Assert.That(politics.TryGetPolitics(out var state), Is.True);
            Assert.That(state.Comp.ShipWithdrawals[first].Id, Is.EqualTo(initial.Id));
            Assert.That(state.Comp.ShipWithdrawals[first].Deadline, Is.EqualTo(initial.Deadline));
            Assert.That(state.Comp.ShipWithdrawals[first].Status, Is.EqualTo(SWGlobalWarWithdrawalStatus.WaitingForPoint));
            Assert.That(server.EntMan.GetComponent<TransformComponent>(first).Coordinates, Is.EqualTo(firstPosition));
            Assert.That(server.EntMan.GetComponent<TransformComponent>(firstCargo).GridUid, Is.EqualTo(first));
            Assert.That(planets.TryGetPhysicalLocation(firstSailor, out var ownPersonPlanet, out _), Is.True);
            Assert.That(ownPersonPlanet, Is.EqualTo(rCapital), "The independent body group can leave a blocked ship.");
            if (independent)
            {
                Assert.That(planets.TryGetPhysicalLocation(second, out var arrived, out _), Is.True);
                Assert.That(arrived, Is.EqualTo(eCapital));
                Assert.That(state.Comp.ShipWithdrawals, Does.Not.ContainKey(second));
                Assert.That(server.EntMan.GetComponent<TransformComponent>(secondSailor).GridUid, Is.EqualTo(second));
                Assert.That(server.EntMan.GetComponent<TransformComponent>(secondCargo).GridUid, Is.EqualTo(second));
            }
            else
            {
                var operation = Guid.NewGuid();
                Assert.That(ownership.CommitOwnership(first, generation, initial.OwnershipRevision, operation,
                    SWGlobalWarFaction.Empire), Is.True);
                Assert.That(ownership.CommitOwnership(first, generation, initial.OwnershipRevision, operation,
                    SWGlobalWarFaction.Empire), Is.True, "Exact capture receipt replay is idempotent.");
                Assert.That(ownership.TryGetOwner(first, generation, out var owner, out var revision), Is.True);
                Assert.That(owner, Is.EqualTo(SWGlobalWarFaction.Empire));
                Assert.That(revision, Is.EqualTo(initial.OwnershipRevision + 1));
                Assert.That(ownership.RegisterShip(first, generation, SWGlobalWarFaction.Republic), Is.False);
                Assert.That(ownership.CommitOwnership(first, generation, initial.OwnershipRevision, Guid.NewGuid(),
                    SWGlobalWarFaction.Republic), Is.False);
                Assert.That(ownership.CommitOwnership(first, Guid.NewGuid(), revision, Guid.NewGuid(),
                    SWGlobalWarFaction.Republic), Is.False);
                Assert.That(state.Comp.ShipWithdrawals, Does.Not.ContainKey(first));
                Assert.That(politics.ValidateShipWithdrawal(initial, rCapital), Is.False);
                Assert.That(server.System<SWGlobalWarTravelSystem>().TryReturnWithdrawnShip(initial, out _),
                    Is.EqualTo(SWGlobalWarWithdrawalStatus.WaitingForSeparation));
                Assert.That(server.EntMan.GetComponent<TransformComponent>(first).Coordinates, Is.EqualTo(firstPosition));
            }
            foreach (var exclusion in exclusions)
                server.EntMan.DeleteEntity(exclusion);
        });
        await pair.RunSeconds(6);
        await server.WaitAssertion(() =>
        {
            if (independent)
            {
                Assert.That(planets.TryGetPhysicalLocation(first, out var arrived, out _), Is.True);
                Assert.That(arrived, Is.EqualTo(rCapital));
                Assert.That(server.EntMan.GetComponent<TransformComponent>(firstCargo).GridUid, Is.EqualTo(first));
                Assert.That(planets.TryGetPhysicalLocation(second, out var stayed, out _), Is.True);
                Assert.That(stayed, Is.EqualTo(eCapital));
                return;
            }
            Assert.That(server.EntMan.GetComponent<TransformComponent>(first).Coordinates, Is.EqualTo(firstPosition),
                "Removing the old destination obstacle cannot revive the former owner's order.");
            Assert.That(ownership.TryGetOwner(first, generation, out _, out var revision), Is.True);
            Assert.That(ownership.CommitOwnership(first, generation, revision, Guid.NewGuid(), SWGlobalWarFaction.Republic), Is.True);
            Assert.That(politics.TryGetPolitics(out var state), Is.True);
            replacement = state.Comp.ShipWithdrawals[first];
            Assert.That(replacement.Id, Is.Not.EqualTo(initial.Id));
            Assert.That(replacement.Deadline, Is.GreaterThan(initial.Deadline));
            Assert.That(politics.ValidateShipWithdrawal(initial, rCapital), Is.False);
        });
        if (independent)
            return;
        await pair.RunSeconds(2);
        await server.WaitAssertion(() => Assert.That(server.EntMan.GetComponent<TransformComponent>(first).Coordinates,
            Is.EqualTo(firstPosition), "The newly captured ship gets its own deadline."));
        await pair.RunSeconds(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(planets.TryGetPhysicalLocation(first, out var arrived, out _), Is.True);
            Assert.That(arrived, Is.EqualTo(rCapital));
            Assert.That(server.EntMan.GetComponent<TransformComponent>(firstCargo).GridUid, Is.EqualTo(first));
            Assert.That(politics.TryGetPolitics(out var state), Is.True);
            Assert.That(state.Comp.ShipWithdrawals, Does.Not.ContainKey(first));
        });
    }

    private static async Task CheckDockedWithdrawal(TestPair pair, Guid generation, EntityUid rule,
        EntityUid ownShip, EntityUid foreignShip, EntityUid ownPort, EntityUid foreignPort, EntityUid ownCargo,
        EntityUid foreignPassenger,
        Func<int, SWGlobalWarPoliticalAction, bool> act, Func<EntityUid, float, EntityUid> createGrid)
    {
        var server = pair.Server;
        var docking = server.System<DockingSystem>();
        var planets = server.System<SWGlobalWarPlanetSystem>();
        var ownership = server.System<SWGlobalWarShipOwnershipSystem>();
        var transform = server.System<SharedTransformSystem>();
        var mapSystem = server.System<Robust.Server.GameObjects.MapSystem>();
        var exclusions = new List<EntityUid>();
        EntityUid thirdShip = default;
        EntityUid secondForeignPort = default;
        EntityUid thirdPort = default;
        EntityUid foreignCargo = default;
        string? ownJoint = null;
        string? foreignJoint = null;
        EntityCoordinates ownPosition = default;
        EntityCoordinates foreignPosition = default;
        EntityCoordinates thirdPosition = default;
        SWGlobalWarShipWithdrawalOrder order = default!;
        var politics = server.System<SWGlobalWarPoliticsSystem>();
        await pair.RunSeconds(35);
        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.HasComponent<FTLComponent>(ownShip), Is.False);
            Assert.That(server.EntMan.GetComponent<DockingComponent>(ownPort).DockedWith, Is.EqualTo(foreignPort));
            Assert.That(server.EntMan.GetComponent<DockingComponent>(foreignPort).DockedWith, Is.EqualTo(ownPort));
            Assert.That(ownership.RegisterShip(foreignShip, generation, SWGlobalWarFaction.Empire), Is.True);
            var map = server.EntMan.GetComponent<TransformComponent>(foreignShip).MapUid!.Value;
            mapSystem.SetTile(foreignShip, server.EntMan.GetComponent<MapGridComponent>(foreignShip),
                new Vector2i(0, 3), new Tile(1));
            secondForeignPort = server.EntMan.SpawnEntity("AirlockShuttle", new EntityCoordinates(foreignShip, .5f, 3.5f));
            transform.SetLocalRotation(secondForeignPort, Angle.FromDegrees(180));
            thirdShip = createGrid(map, 2000);
            thirdPort = server.EntMan.SpawnEntity("AirlockShuttle", new EntityCoordinates(thirdShip, .5f, .5f));
            Assert.That(ownership.RegisterShip(thirdShip, generation, SWGlobalWarFaction.Empire), Is.True);
            var config = docking.GetDockingConfig(thirdShip, foreignShip);
            Assert.That(config, Is.Not.Null);
            var dockPosition = transform.ToMapCoordinates(config!.Coordinates);
            transform.SetCoordinates(thirdShip, server.EntMan.GetComponent<TransformComponent>(thirdShip),
                new EntityCoordinates(map, dockPosition.Position),
                rotation: config.Angle + transform.GetWorldRotation(config.Coordinates.EntityId));
            foreach (var ports in config.Docks)
            {
                Assert.That(docking.CanDock((ports.DockAUid, ports.DockA), (ports.DockBUid, ports.DockB)), Is.True);
                docking.Dock((ports.DockAUid, ports.DockA), (ports.DockBUid, ports.DockB));
            }
            foreignCargo = server.EntMan.SpawnEntity("Pen", new EntityCoordinates(foreignShip, .5f, 2.5f));
            transform.SetCoordinates(foreignPassenger, new EntityCoordinates(foreignShip, .5f, 1.5f));
        });
        await pair.RunSeconds(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.GetComponent<DockingComponent>(secondForeignPort).DockedWith, Is.EqualTo(thirdPort));
            Assert.That(server.EntMan.GetComponent<DockingComponent>(thirdPort).DockedWith, Is.EqualTo(secondForeignPort));
            ownJoint = server.EntMan.GetComponent<DockingComponent>(ownPort).DockJointId;
            foreignJoint = server.EntMan.GetComponent<DockingComponent>(thirdPort).DockJointId;
            Assert.That(ownJoint, Is.Not.Null);
            Assert.That(foreignJoint, Is.Not.Null);
            ownPosition = server.EntMan.GetComponent<TransformComponent>(ownShip).Coordinates;
            foreignPosition = server.EntMan.GetComponent<TransformComponent>(foreignShip).Coordinates;
            thirdPosition = server.EntMan.GetComponent<TransformComponent>(thirdShip).Coordinates;
            foreach (var planet in server.EntMan.GetComponent<SWGlobalWarGameruleComponent>(rule).PlanetEntities.Values)
            {
                if (server.EntMan.GetComponent<SWGlobalWarPlanetComponent>(planet).OrbitOwner != SWGlobalWarFaction.Republic)
                    continue;
                Assert.That(planets.TryEnsureLocation(planet, SWGlobalWarPlanetLocation.Orbit, out var map, out _), Is.True);
                exclusions.Add(server.EntMan.SpawnEntity("TravelTestExclusion", new EntityCoordinates(map!.Value, default)));
            }
            Assert.That(act(0, SWGlobalWarPoliticalAction.OfferTruce), Is.True);
            Assert.That(act(1, SWGlobalWarPoliticalAction.OfferTruce), Is.True);
            Assert.That(politics.TryGetPolitics(out var state), Is.True);
            order = state.Comp.ShipWithdrawals[ownShip];
        });
        await pair.RunSeconds(4);
        await server.WaitAssertion(() =>
        {
            Assert.That(politics.TryGetPolitics(out var state), Is.True);
            Assert.That(state.Comp.ShipWithdrawals[ownShip].Id, Is.EqualTo(order.Id));
            Assert.That(state.Comp.ShipWithdrawals[ownShip].Deadline, Is.EqualTo(order.Deadline));
            Assert.That(state.Comp.ShipWithdrawals[ownShip].Status, Is.EqualTo(SWGlobalWarWithdrawalStatus.WaitingForPoint));
            Assert.That(server.EntMan.GetComponent<TransformComponent>(ownShip).Coordinates, Is.EqualTo(ownPosition));
            Assert.That(server.EntMan.GetComponent<DockingComponent>(ownPort).DockedWith, Is.EqualTo(foreignPort));
            Assert.That(server.EntMan.GetComponent<DockingComponent>(ownPort).DockJointId, Is.EqualTo(ownJoint));
            AssertForeignGroup();
            foreach (var exclusion in exclusions)
                server.EntMan.DeleteEntity(exclusion);
        });
        await pair.RunSeconds(6);
        await server.WaitAssertion(() =>
        {
            Assert.That(planets.TryGetPhysicalLocation(ownShip, out var destination, out var location), Is.True);
            Assert.That(location, Is.EqualTo(SWGlobalWarPlanetLocation.Orbit));
            Assert.That(server.EntMan.GetComponent<SWGlobalWarPlanetComponent>(destination).OrbitOwner, Is.EqualTo(SWGlobalWarFaction.Republic));
            Assert.That(server.EntMan.GetComponent<DockingComponent>(ownPort).DockedWith, Is.Null);
            Assert.That(server.EntMan.GetComponent<DockingComponent>(foreignPort).DockedWith, Is.Null);
            Assert.That(server.EntMan.GetComponent<TransformComponent>(ownCargo).GridUid, Is.EqualTo(ownShip));
            Assert.That(politics.TryGetPolitics(out var state), Is.True);
            Assert.That(state.Comp.ShipWithdrawals, Does.Not.ContainKey(ownShip));
            AssertForeignGroup();
        });

        void AssertForeignGroup()
        {
            Assert.That(server.EntMan.GetComponent<TransformComponent>(foreignShip).Coordinates, Is.EqualTo(foreignPosition));
            Assert.That(server.EntMan.GetComponent<TransformComponent>(thirdShip).Coordinates, Is.EqualTo(thirdPosition));
            Assert.That(server.EntMan.GetComponent<DockingComponent>(secondForeignPort).DockedWith, Is.EqualTo(thirdPort));
            Assert.That(server.EntMan.GetComponent<DockingComponent>(thirdPort).DockedWith, Is.EqualTo(secondForeignPort));
            Assert.That(server.EntMan.GetComponent<DockingComponent>(thirdPort).DockJointId, Is.EqualTo(foreignJoint));
            Assert.That(server.EntMan.GetComponent<TransformComponent>(foreignCargo).GridUid, Is.EqualTo(foreignShip));
            Assert.That(server.EntMan.GetComponent<TransformComponent>(foreignPassenger).GridUid, Is.EqualTo(foreignShip));
        }
    }
}
