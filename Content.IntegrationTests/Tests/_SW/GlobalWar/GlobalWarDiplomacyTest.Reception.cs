using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._SW.GlobalWar;
using Content.Server._SW.GlobalWar.Spawning;
using Content.Server._SW.GlobalWar.Politics;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Body.Components;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Body.Components;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.Power;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Containers;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

public sealed partial class GlobalWarDiplomacyTest
{
    private static async Task CheckReceptionCallback(TestPair pair, string scenario, EntityUid body,
        EntityUid ship, EntityUid planet, EntityUid anchor, Guid generation,
        Func<int, SWGlobalWarPoliticalAction, bool> act)
    {
        var server = pair.Server;
        var spawn = server.System<SWGlobalWarSpawnSystem>();
        var politics = server.System<SWGlobalWarPoliticsSystem>();
        var ownership = server.System<SWGlobalWarShipOwnershipSystem>();
        var probe = server.System<SWPoliticsNotificationProbeSystem>();
        var grid = server.EntMan.GetComponent<TransformComponent>(anchor).ParentUid;
        var map = server.EntMan.GetComponent<TransformComponent>(grid).MapUid!.Value;
        EntityUid door = default;
        Container carrier = default!;
        EntityCoordinates shipPosition = default;
        SWGlobalWarShipWithdrawalOrder order = default!;
        var checks = 0;
        var changed = false;
        await pair.RunSeconds(3);
        await server.WaitAssertion(() =>
        {
            for (var y = 100; y < 107; y++)
                if (y != 103)
                    server.EntMan.SpawnEntity("WallSolid", new EntityCoordinates(grid, 107.5f, y + .5f));
            door = server.EntMan.SpawnEntity("Airlock", new EntityCoordinates(grid, 107.5f, 103.5f));
            var power = new PowerChangedEvent(true, 1000);
            server.EntMan.EventBus.RaiseLocalEvent(door, ref power);
            server.EntMan.AddComponent<SWWithdrawalDoorProbeComponent>(door);
            var crate = server.EntMan.SpawnEntity("CrateGeneric", new EntityCoordinates(ship, 3.5f, 2.5f));
            var containers = server.System<SharedContainerSystem>();
            carrier = containers.EnsureContainer<Container>(crate, "callback-passenger");
            Assert.That(containers.Insert(body, carrier, force: true), Is.True);
            shipPosition = server.EntMan.GetComponent<TransformComponent>(ship).Coordinates;
            Assert.That(politics.TryGetPolitics(out var state), Is.True);
            order = state.Comp.ShipWithdrawals[ship];
        });
        await pair.RunSeconds(3);
        await server.WaitAssertion(() =>
        {
            var normal = new GasMixture(Atmospherics.CellVolume) { Temperature = Atmospherics.T20C };
            normal.SetMoles(Gas.Oxygen, 21.824f);
            normal.SetMoles(Gas.Nitrogen, 82.103f);
            var atmos = server.System<AtmosphereSystem>();
            for (var x = 100; x < 115; x++)
            for (var y = 100; y < 107; y++)
                if (atmos.GetTileMixture(grid, map, new Vector2i(x, y)) is { Immutable: false } gas)
                    gas.CopyFrom(normal);
            Assert.That(server.System<SharedDoorSystem>().CanOpen(door, user: body), Is.True);
            Assert.That(spawn.TryPrepareBodyReturn(body, generation, out var plan, planet, SWGlobalWarPlanetLocation.Orbit), Is.True);
            Assert.That(spawn.ValidateBodyReturn(plan), Is.True);
            spawn.ReleaseBodyReturn(plan);
            probe.DoorChecked = uid =>
            {
                if (uid != door || changed || ++checks != 2)
                    return;
                // Preparation has succeeded. Change authorization inside the repeated door validation.
                changed = true;
                if (scenario.EndsWith("-war", StringComparison.Ordinal))
                    Assert.That(act(0, SWGlobalWarPoliticalAction.BreakTruce), Is.False,
                        "A nested political command is rejected while the withdrawal update is executing.");
                else
                    Assert.That(ownership.CommitOwnership(ship, generation, order.OwnershipRevision,
                        Guid.NewGuid(), SWGlobalWarFaction.Empire), Is.True);
            };
        });
        await pair.RunSeconds(121);
        await server.WaitAssertion(() =>
        {
            Assert.That(changed, Is.True, "The real door callback must run during repeated validation.");
            Assert.That(politics.TryGetPolitics(out var state), Is.True);
            if (scenario.EndsWith("-war", StringComparison.Ordinal))
            {
                Assert.That(state.Comp.Relations, Is.EqualTo(SWGlobalWarRelations.Truce));
                Assert.That(carrier.Contains(body), Is.False);
                Assert.That(server.EntMan.GetComponent<TransformComponent>(body).GridUid, Is.EqualTo(grid));
                Assert.That(server.EntMan.GetComponent<TransformComponent>(ship).Coordinates, Is.Not.EqualTo(shipPosition));
                Assert.That(act(0, SWGlobalWarPoliticalAction.BreakTruce), Is.True,
                    "The same current head can resume war outside the protected update.");
            }
            else
            {
                Assert.That(carrier.Contains(body), Is.True, "Authorization changed before the first detachment.");
                Assert.That(server.EntMan.GetComponent<TransformComponent>(ship).Coordinates, Is.EqualTo(shipPosition));
            }
            Assert.That(state.Comp.ShipWithdrawals, Does.Not.ContainKey(ship), "A snapshot must not resurrect the cancelled order.");
            Assert.That(server.EntMan.GetComponent<SWGlobalWarPlanetMapComponent>(map).Leases.Values
                .Any(lease => lease.Reason == "withdrawal-return"), Is.False);
            probe.DoorChecked = null;
        });
    }

    private static async Task CheckReceptionPressureFlow(TestPair pair, EntityUid body, EntityUid ship,
        EntityUid planet, EntityUid anchor, Guid generation, Func<int, SWGlobalWarPoliticalAction, bool> act)
    {
        var server = pair.Server;
        var atmos = server.System<AtmosphereSystem>();
        var spawn = server.System<SWGlobalWarSpawnSystem>();
        var maps = server.System<Robust.Server.GameObjects.MapSystem>();
        var coordinates = server.EntMan.GetComponent<TransformComponent>(anchor).Coordinates;
        var grid = coordinates.EntityId;
        var map = server.EntMan.GetComponent<TransformComponent>(grid).MapUid!.Value;
        var gridData = server.EntMan.GetComponent<MapGridComponent>(grid);
        var cell = new Vector2i(103, 103);
        var feed = new Vector2i(104, 103);
        var oldWind = server.CfgMan.GetCVar(Content.Shared.CCVar.CCVars.SpaceWind);
        var movement = server.EntMan.GetComponent<MovedByPressureComponent>(body);
        var oldMovement = movement.Enabled;
        var normal = new GasMixture(Atmospherics.CellVolume) { Temperature = Atmospherics.T20C };
        normal.SetMoles(Gas.Oxygen, 21.824f);
        normal.SetMoles(Gas.Nitrogen, 82.103f);
        var high = new GasMixture(Atmospherics.CellVolume) { Temperature = Atmospherics.T20C };
        high.SetMoles(Gas.Oxygen, 2000000);
        high.SetMoles(Gas.Nitrogen, 8000000);
        Container carrier = default!;
        EntityCoordinates shipPosition = default;
        SWGlobalWarReturnPlan prepared = default!;
        await pair.RunSeconds(3);
        await server.WaitAssertion(() =>
        {
            server.CfgMan.SetCVar(Content.Shared.CCVar.CCVars.SpaceWind, true);
            movement.Enabled = true;
            var crate = server.EntMan.SpawnEntity("CrateGeneric", new EntityCoordinates(ship, 3.5f, 2.5f));
            var containers = server.System<SharedContainerSystem>();
            carrier = containers.EnsureContainer<Container>(crate, "pressure-passenger");
            Assert.That(containers.Insert(body, carrier, force: true), Is.True);
            shipPosition = server.EntMan.GetComponent<TransformComponent>(ship).Coordinates;
            Assert.That(spawn.TryPrepareBodyReturn(body, generation, out prepared, planet, SWGlobalWarPlanetLocation.Orbit), Is.True);
            Assert.That(atmos.GetTileMixture(grid, map, feed)!.Immutable, Is.False);
            atmos.GetTileMixture(grid, map, feed)!.CopyFrom(high);
        });

        var observed = false;
        var observedDifference = 0f;
        for (var tick = 0; tick < 240 && !observed; tick++)
        {
            await pair.RunTicksSync(1);
            await server.WaitPost(() =>
            {
                observed = atmos.TryGetKnownTilePressureState(grid, cell, out var pending, out var moving,
                    out observedDifference, out _) && !pending && moving && atmos.CanPressureMove(body, observedDifference);
                if (!observed)
                    atmos.GetTileMixture(grid, map, feed)!.CopyFrom(high);
            });
        }
        await server.WaitAssertion(() =>
        {
            Assert.That(observed, Is.True, $"Real atmospheric solver must produce a dangerous flow; last difference={observedDifference}.");
            Assert.That(spawn.ValidateBodyReturn(prepared), Is.False);
            spawn.ReleaseBodyReturn(prepared);
            Assert.That(carrier.Contains(body), Is.True);
            Assert.That(act(0, SWGlobalWarPoliticalAction.OfferTruce), Is.True);
            Assert.That(act(1, SWGlobalWarPoliticalAction.OfferTruce), Is.True);
        });
        // Keep the actual leak supplied across the short test withdrawal deadline, without changing solver state directly.
        for (var tick = 0; tick < 240; tick++)
        {
            await server.WaitPost(() => atmos.GetTileMixture(grid, map, feed)!.CopyFrom(high));
            await pair.RunTicksSync(1);
        }
        await server.WaitAssertion(() =>
        {
            Assert.That(carrier.Contains(body), Is.True, "Unsafe reception must not detach its passenger at the deadline.");
            Assert.That(server.EntMan.GetComponent<TransformComponent>(ship).Coordinates, Is.EqualTo(shipPosition));
            var politics = server.System<SWGlobalWarPoliticsSystem>();
            Assert.That(politics.TryGetPolitics(out var state), Is.True);
            Assert.That(state.Comp.ShipWithdrawals, Does.ContainKey(ship));
            var clock = server.System<SWGlobalWarCampaignSystem>();
            Assert.That(clock.TryGetCampaign(out var campaign), Is.True);
            Assert.That(clock.Elapsed(campaign), Is.GreaterThanOrEqualTo(state.Comp.ShipWithdrawals[ship].Deadline));
            foreach (var tile in maps.GetAllTiles(grid, gridData))
                atmos.GetTileMixture(grid, map, tile.GridIndices)!.CopyFrom(normal);
        });
        await pair.RunSeconds(6);
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(Content.Shared.CCVar.CCVars.SpaceWind, oldWind);
            movement.Enabled = oldMovement;
        });
    }

    private static async Task<EntityUid> CheckReceptionSafety(TestPair pair, EntityUid body, EntityUid planet,
        SWGlobalWarPlanetLocation location, EntityUid anchor, Guid generation)
    {
        var server = pair.Server;
        var spawn = server.System<SWGlobalWarSpawnSystem>();
        var gate = server.System<SWGlobalWarReceptionSystem>();
        var atmos = server.System<AtmosphereSystem>();
        var maps = server.System<Robust.Server.GameObjects.MapSystem>();
        var coordinates = server.EntMan.GetComponent<TransformComponent>(anchor).Coordinates;
        var grid = coordinates.EntityId;
        var gridData = server.EntMan.GetComponent<MapGridComponent>(grid);
        var map = server.EntMan.GetComponent<TransformComponent>(grid).MapUid!.Value;
        var originalParent = server.EntMan.GetComponent<TransformComponent>(body).ParentUid;
        var originalWind = server.CfgMan.GetCVar(Content.Shared.CCVar.CCVars.SpaceWind);
        var movedByPressure = server.EntMan.GetComponent<MovedByPressureComponent>(body);
        var originalMoved = movedByPressure.Enabled;
        SWGlobalWarReturnPlan prepared = default!;

        GasMixture Air(float temperature = Atmospherics.T20C, float oxygen = 21.824f, float nitrogen = 82.103f)
        {
            var gas = new GasMixture(Atmospherics.CellVolume) { Temperature = temperature };
            gas.SetMoles(Gas.Oxygen, oxygen);
            gas.SetMoles(Gas.Nitrogen, nitrogen);
            return gas;
        }
        void SetReceptionAir(GasMixture gas)
        {
            // The map's ambient gas does not instantly replace mutable floor mixtures. Set the actual test samples
            // explicitly; these assertions test suitability, not the time taken by a life-support plant to recover.
            for (var x = 100; x < 115; x++)
            for (var y = 100; y < 107; y++)
                atmos.GetTileMixture(grid, map, new Vector2i(x, y), excite: false)?.CopyFrom(gas);
        }
        void Prepare(bool expected, string reason)
        {
            var bodyBefore = server.EntMan.GetComponent<TransformComponent>(body).Coordinates;
            var before = atmos.GetTileMixture(grid, map, new Vector2i(103, 103))!.TotalMoles;
            var result = spawn.TryPrepareBodyReturn(body, generation, out var plan, planet, location);
            if (expected && !result)
            {
                var tx = server.EntMan.GetComponent<TransformComponent>(anchor);
                var lookup = server.System<EntityLookupSystem>();
                var bounds = lookup.GetAABBNoContainer(body, Vector2.Zero, Angle.Zero).Enlarged(.05f);
                var known = atmos.TryGetKnownTilePressureState(grid, new Vector2i(103, 103),
                    out var pending, out var moving, out var difference, out _);
                reason += $" Reception diagnostic: anchor={tx.Coordinates}; grid={grid}; map={map}; " +
                    $"known={known} pending={pending} moving={moving} difference={difference}; " +
                    $"gas={atmos.GetTileMixture(grid, map, new Vector2i(103, 103))?.Pressure}; " +
                    $"gate={gate.IsSuitable(body, planet, anchor, coordinates, bounds, _ => false)}";
            }
            Assert.That(result, Is.EqualTo(expected), reason);
            if (result)
            {
                Assert.That(spawn.ValidateBodyReturn(plan), Is.True, reason);
                spawn.ReleaseBodyReturn(plan);
            }
            Assert.That(atmos.GetTileMixture(grid, map, new Vector2i(103, 103))!.TotalMoles, Is.EqualTo(before),
                "Suitability must not consume destination gas.");
            Assert.That(server.EntMan.GetComponent<TransformComponent>(body).Coordinates, Is.EqualTo(bodyBefore),
                "Preparing and releasing a return must not move its passenger.");
            Assert.That(server.EntMan.GetComponent<TransformComponent>(body).ParentUid, Is.EqualTo(originalParent),
                $"Preparation must not move its passenger: originalParent={originalParent}; before={bodyBefore}; " +
                $"after={server.EntMan.GetComponent<TransformComponent>(body).Coordinates}.");
        }

        await pair.RunSeconds(3);
        await server.WaitAssertion(() =>
        {
            // Godmode protects health in this geometry fixture, but must not disable the physics being tested.
            Assert.That(server.EntMan.GetComponent<TransformComponent>(body).ParentUid, Is.EqualTo(originalParent),
                "The isolated ship must retain its passenger during the initial physics steps.");
            TestContext.Out.WriteLine($"Reception precheck: originalParent={originalParent}; originalWind={originalWind}; " +
                $"body={server.EntMan.GetComponent<TransformComponent>(body).Coordinates}; " +
                $"source={server.EntMan.GetComponent<TransformComponent>(originalParent).Coordinates}; map={map}.");
            server.CfgMan.SetCVar(Content.Shared.CCVar.CCVars.SpaceWind, true);
            var pressureFixture = server.EntMan.SpawnEntity("CrateGeneric", new EntityCoordinates(grid, 80, 80));
            var resistance = server.EntMan.EnsureComponent<MovedByPressureComponent>(pressureFixture);
            resistance.PressureResistance = 1000;
            resistance.MoveResist = 100;
            var physics = server.System<Robust.Shared.Physics.Systems.SharedPhysicsSystem>();
            physics.SetBodyType(pressureFixture, Robust.Shared.Physics.BodyType.Static);
            Assert.That(atmos.CanPressureMove(pressureFixture, 2000), Is.True,
                "Static branch uses force even when probability is only five.");
            Assert.That(atmos.CanPressureMove(pressureFixture, 1900), Is.False);
            physics.SetBodyType(pressureFixture, Robust.Shared.Physics.BodyType.Dynamic);
            Assert.That(atmos.CanPressureMove(pressureFixture, 2000), Is.False);
            Assert.That(atmos.CanPressureMove(pressureFixture, 5000), Is.False, "Probability boundary is strict.");
            Assert.That(atmos.CanPressureMove(pressureFixture, 5001), Is.True);
            server.EntMan.DeleteEntity(pressureFixture);
            server.EntMan.GetComponent<MovedByPressureComponent>(body).Enabled = true;
            Prepare(true, "Prepared public reception with suitable atmosphere.");
            Assert.That(atmos.CanPressureMove(body, .3944f), Is.False,
                "Residual solver equalization is below this body's actual pressure-movement resistance.");
            Assert.That(atmos.CanPressureMove(body, 100000000), Is.True,
                "The read-only query still recognizes pressure capable of moving this body.");
            movedByPressure.Enabled = false;
            Assert.That(atmos.CanPressureMove(body, 100000000), Is.False);
            movedByPressure.Enabled = true;
            server.CfgMan.SetCVar(Content.Shared.CCVar.CCVars.SpaceWind, false);
            Assert.That(atmos.CanPressureMove(body, 100000000), Is.False);
            server.CfgMan.SetCVar(Content.Shared.CCVar.CCVars.SpaceWind, true);
            Assert.That(spawn.TryPrepareBodyReturn(body, generation, out prepared, planet, location), Is.True);
            Assert.That(server.EntMan.GetComponent<SWGlobalWarPlanetMapComponent>(map).Leases, Does.ContainKey(prepared.Id));
            Prepare(false, "A real occupied footprint cannot be reserved twice.");
            maps.SetTile(grid, gridData, new Vector2i(103, 103), Tile.Empty);
            Assert.That(spawn.ValidateBodyReturn(prepared), Is.False, "Floor changed after reservation.");
            spawn.ReleaseBodyReturn(prepared);
            Assert.That(server.EntMan.GetComponent<SWGlobalWarPlanetMapComponent>(map).Leases, Does.Not.ContainKey(prepared.Id));
            maps.SetTile(grid, gridData, new Vector2i(103, 103), new Tile(1));
            // No test teleport of the marker: the reception system owns recovery from its saved stationary binding.
        });
        await pair.RunSeconds(3);
        await server.WaitAssertion(() => Prepare(true, "Repaired floor becomes usable after atmosphere revalidation."));

        foreach (var (gas, reason) in new[]
                 {
                     (Air(oxygen: 0, nitrogen: 0), "Vacuum"),
                     (Air(oxygen: 0), "No metabolizable oxygen"),
                     (Air(temperature: 500), "Hot air"),
                     (Air(temperature: 200), "Cold air"),
                     (Air(oxygen: 200, nitrogen: 800), "Excessive pressure"),
                 })
        {
            await server.WaitAssertion(() =>
            {
                SetReceptionAir(Air());
                Prepare(true, $"Positive control before {reason}.");
                Assert.That(spawn.TryPrepareBodyReturn(body, generation, out prepared, planet, location), Is.True);
                atmos.SetMapAtmosphere(map, false, gas);
                Assert.That(spawn.ValidateBodyReturn(prepared), Is.False, "Pending revalidation is not safety.");
                spawn.ReleaseBodyReturn(prepared);
            });
            await pair.RunSeconds(3);
            await server.WaitAssertion(() =>
            {
                SetReceptionAir(gas);
                Prepare(false, reason);
            });
            await server.WaitPost(() => atmos.SetMapAtmosphere(map, false, Air()));
            await pair.RunSeconds(3);
        }

        await server.WaitPost(() => SetReceptionAir(Air()));

        EntityUid door = default;
        var walls = new List<EntityUid>();
        await server.WaitAssertion(() =>
        {
            for (var y = 100; y < 107; y++)
                if (y != 103)
                    walls.Add(server.EntMan.SpawnEntity("WallSolid", new EntityCoordinates(grid, 107.5f, y + .5f)));
            door = server.EntMan.SpawnEntity("Airlock", new EntityCoordinates(grid, 107.5f, 103.5f));
            var plating = new Tile(server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId);
            maps.SetTile(grid, gridData, new Vector2i(108, 103), plating);
            // Real airlock handlers receive fixture power; this is not a life-support or power-network playtest.
            var power = new PowerChangedEvent(true, 1000);
            server.EntMan.EventBus.RaiseLocalEvent(door, ref power);
        });
        await pair.RunSeconds(3);
        await server.WaitAssertion(() =>
        {
            var doors = server.System<SharedDoorSystem>();
            var access = server.System<AccessReaderSystem>();
            Assert.That(access.GetMainAccessReader(door, out var reader), Is.True);
            var privateAccess = server.EntMan.EnsureComponent<AccessComponent>(body);
            Assert.That(server.System<SharedAccessSystem>().TrySetTags(body, ["Command"], privateAccess), Is.True);
            Assert.That(doors.CanOpen(door, user: body), Is.True, "Fixture door must genuinely be openable.");
            Prepare(true, "A closed powered public airlock permits a safe exit.");
            Assert.That(spawn.TryPrepareBodyReturn(body, generation, out prepared, planet, location), Is.True);
            access.TrySetAccesses(reader!.Value, new List<Robust.Shared.Prototypes.ProtoId<Content.Shared.Access.AccessLevelPrototype>> { "Command" });
            Assert.That(doors.CanOpen(door, user: body), Is.True, "The captain fixture has private Command access.");
            Assert.That(spawn.ValidateBodyReturn(prepared), Is.False, "A newly private door invalidates the prepared route.");
            spawn.ReleaseBodyReturn(prepared);
            Prepare(false, "Personal military access does not make a corridor public.");
            access.TrySetAccesses(reader.Value, new List<Robust.Shared.Prototypes.ProtoId<Content.Shared.Access.AccessLevelPrototype>>());
            var power = new PowerChangedEvent(false, 0);
            server.EntMan.EventBus.RaiseLocalEvent(door, ref power);
            Prepare(false, "Closed unpowered exit must not require prying.");
            power = new PowerChangedEvent(true, 1000);
            server.EntMan.EventBus.RaiseLocalEvent(door, ref power);
            doors.SetState(door, DoorState.Welded);
            Prepare(false, "Welded exit.");
            doors.SetState(door, DoorState.Closed);
            Prepare(true, "Public exit restored.");

            var rightAir = atmos.GetTileMixture(grid, map, new Vector2i(108, 103))!;
            Assert.That(rightAir.Immutable, Is.False, "The pressure fixture needs real local tile air.");
            var originalAir = rightAir.Clone();
            rightAir.CopyFrom(Air(oxygen: 21.824f * 5, nitrogen: 82.103f * 5));
            Assert.That(rightAir.Pressure, Is.InRange(490, 510));
            Assert.That(doors.CanOpen(door, user: body), Is.True);
            Assert.That(server.System<Content.Server.Doors.Systems.FirelockSystem>().CheckDoorPressureAndFire(door).Pressure, Is.True);
            Prepare(false, "A closed ordinary airlock holds approximately 100/500 kPa despite individually breathable sides.");
            rightAir.CopyFrom(originalAir);
            Prepare(true, "Equalizing both sides restores the same public route.");

            var paper = server.EntMan.SpawnEntity("Paper", coordinates);
            Assert.That(server.EntMan.HasComponent<FlammableComponent>(paper), Is.True);
            server.System<FlammableSystem>().SetFireStacks(paper, 2, ignite: true);
            Assert.That(server.EntMan.GetComponent<FlammableComponent>(paper).OnFire, Is.True);
            Prepare(false, "Burning item on the reception footprint.");
            server.EntMan.DeleteEntity(paper);
            Prepare(true, "Removing the burning item restores the footprint.");

            var duplicate = server.EntMan.SpawnEntity("SWWithdrawalTestReception", coordinates.Offset(new Vector2(0, 2)));
            Prepare(false, "Ambiguous reception address cannot select a convenient duplicate.");
            server.EntMan.DeleteEntity(duplicate);
            Prepare(true, "Unambiguous original address restored.");
            Assert.That(spawn.TryPrepareBodyReturn(body, Guid.NewGuid(), out _, planet, location), Is.False);

            var droid = server.EntMan.SpawnEntity("SWMobDroid", new EntityCoordinates(grid, 90, 90));
            Assert.That(server.EntMan.HasComponent<BodyComponent>(droid), Is.False);
            Assert.That(server.EntMan.HasComponent<RespiratorComponent>(droid), Is.False);
            var lookup = server.System<EntityLookupSystem>();
            var bounds = lookup.GetAABBNoContainer(droid, Vector2.Zero, Angle.Zero).Enlarged(.05f);
            Assert.That(gate.IsSuitable(droid, planet, anchor, coordinates, bounds, _ => false), Is.True,
                "Known SW robotic physiology does not need organic lungs.");
            var unknown = server.EntMan.SpawnEntity("MobHuman", new EntityCoordinates(grid, 95, 95));
            server.EntMan.RemoveComponent<RespiratorComponent>(unknown);
            bounds = lookup.GetAABBNoContainer(unknown, Vector2.Zero, Angle.Zero).Enlarged(.05f);
            Assert.That(gate.IsSuitable(unknown, planet, anchor, coordinates, bounds, _ => false), Is.False,
                "Missing respiration on an organic body is not proof of a supported non-breather.");
            server.EntMan.DeleteEntity(unknown);
            server.EntMan.DeleteEntity(droid);
        });
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(Content.Shared.CCVar.CCVars.SpaceWind, originalWind);
            movedByPressure.Enabled = originalMoved;
        });
        return door;
    }
}
