#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.Server._SW.BattlePass;
using Content.Server.Cargo.Systems;
using Content.Server.Players.PlayTimeTracking;
using Content.Server.Preferences.Managers;
using Content.Server.Station.Systems;
using Content.Shared._SW.BattlePass;
using Content.Shared._SW.Cosmetics;
using Content.Shared._SW.Loadouts;
using Content.Shared.Armor;
using Content.Shared.CCVar;
using Content.Shared.Clothing;
using Content.Shared.Clothing.Components;
using Content.Shared.GameTicking;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Item;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Preferences.Loadouts.Effects;
using Content.Shared.Roles;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._SW;

/// <summary>
/// The whole of the job loadouts, through the same spawn a player gets.
/// </summary>
[TestFixture]
[TestOf(typeof(SWMergedGear))]
public sealed class SWJobLoadoutsTest
{
    private const string Soldier = "SWRepublicSoldier";
    private const string SoldierRole = "JobSWRepublicSoldier";
    private const string Weapons = "SWWeaponsRepublic";
    private const string Pistol = "SWLoadoutWeaponUIHBP";
    private const string HeavyPistol = "SWLoadoutWeaponM44";
    private const string Rifle = "SWH206Renegade";

    private static IEnumerable<JobPrototype> PlayableJobs(IPrototypeManager protos)
    {
        return SWLobbyJobs.Enumerate(protos)
            .Where(j => j.SetPreference && j.StartingGear != null);
    }

    private static RoleLoadout Choose(RoleLoadoutPrototype role, LoadoutGroupPrototype group, LoadoutPrototype item, HumanoidCharacterProfile profile, IPrototypeManager protos)
    {
        var loadout = new RoleLoadout(role.ID);
        loadout.SetDefault(profile, null, protos, force: true);
        loadout.SelectedLoadouts[group.ID] = new List<Loadout> { new() { Prototype = item.ID } };
        return loadout;
    }

    [Test]
    public async Task EveryJobHasChoices()
    {
        await using var pair = await PoolManager.GetServerClient();
        var protos = pair.Server.ProtoMan;

        await pair.Server.WaitAssertion(() =>
        {
            var jobs = PlayableJobs(protos).ToList();
            Assert.That(jobs, Is.Not.Empty);

            foreach (var job in jobs)
            {
                Assert.That(protos.TryIndex<RoleLoadoutPrototype>(LoadoutSystem.GetJobPrototype(job.ID), out var role), $"{job.ID} has no loadout.");
                var groups = role!.Groups.Select(g => protos.Index(g)).ToList();
                var slots = groups.SelectMany(g => g.Loadouts).Select(l => protos.Index(l)).SelectMany(l => l.Equipment.Keys).ToHashSet();

                Assert.That(slots, Does.Contain("head"), $"{job.ID} offers nothing for the head.");
                Assert.That(slots.Overlaps(["outerClothing", "jumpsuit"]), $"{job.ID} offers no uniform.");

                foreach (var group in groups)
                {
                    // A profile without this job's loadout is filled in with no session at spawn, so no check can run on it.
                    if (group.MinLimit > 0)
                        Assert.That(protos.Index(group.Loadouts[0]).Effects, Is.Empty, $"The default of {group.ID} is locked.");
                }
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Every choice of every job, one at a time: it ends up on the body, and nothing lies on the floor.
    /// </summary>
    [Test]
    public async Task EveryChoiceIsWornAndNothingFalls()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var protos = server.ProtoMan;
        var spawning = server.System<StationSpawningSystem>();
        var inventory = server.System<InventorySystem>();
        var hands = server.System<SharedHandsSystem>();
        var containers = server.System<SharedContainerSystem>();
        var map = await pair.CreateTestMap();

        foreach (var job in PlayableJobs(protos).ToList())
        {
            await server.WaitAssertion(() =>
            {
                var role = protos.Index<RoleLoadoutPrototype>(LoadoutSystem.GetJobPrototype(job.ID));
                var profile = new HumanoidCharacterProfile();

                foreach (var group in role.Groups.Select(g => protos.Index(g)))
                {
                    foreach (var item in group.Loadouts.Select(l => protos.Index(l)))
                    {
                        var mob = spawning.SpawnPlayerMob(map.GridCoords, job.ID, profile.WithLoadout(Choose(role, group, item, profile, protos)), null);

                        foreach (var (slot, wanted) in item.Equipment)
                        {
                            Assert.That(inventory.TryGetSlotEntity(mob, slot, out var worn), $"{job.ID} wears nothing in {slot} with {item.ID}.");
                            Assert.That(entMan.GetComponent<MetaDataComponent>(worn!.Value).EntityPrototype?.ID, Is.EqualTo(wanted.Id), $"{job.ID} with {item.ID}");
                        }

                        foreach (var (slot, wanted) in item.Storage)
                        {
                            Assert.That(inventory.TryGetSlotEntity(mob, slot, out var bag), $"{job.ID} has no {slot} for {item.ID}.");
                            var stored = entMan.GetComponent<StorageComponent>(bag!.Value).Container.ContainedEntities
                                .Select(e => entMan.GetComponent<MetaDataComponent>(e).EntityPrototype?.ID)
                                .ToList();
                            foreach (var one in wanted)
                            {
                                Assert.That(stored, Does.Contain(one.Id), $"{job.ID} with {item.ID}");
                            }
                        }

                        foreach (var one in item.Inhand)
                        {
                            Assert.That(hands.EnumerateHeld(mob).Select(e => entMan.GetComponent<MetaDataComponent>(e).EntityPrototype?.ID), Does.Contain(one.Id), $"{job.ID} with {item.ID}");
                        }

                        var fallen = new List<string>();
                        var query = entMan.EntityQueryEnumerator<ItemComponent, TransformComponent>();
                        while (query.MoveNext(out var uid, out _, out var xform))
                        {
                            if (xform.MapID == map.MapId && !containers.IsEntityInContainer(uid))
                            {
                                fallen.Add(entMan.ToPrettyString(uid).ToString());
                                entMan.DeleteEntity(uid);
                            }
                        }

                        Assert.That(fallen, Is.Empty, $"{job.ID} with {item.ID} dropped gear.");
                        entMan.DeleteEntity(mob);
                    }
                }
            });
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A uniform or a hat changes the look only: armour and speed are those of what the job would wear there.
    /// </summary>
    [Test]
    public async Task LooksGiveNoAdvantage()
    {
        await using var pair = await PoolManager.GetServerClient();
        var protos = pair.Server.ProtoMan;
        var factory = pair.Server.ResolveDependency<IComponentFactory>();

        await pair.Server.WaitAssertion(() =>
        {
            foreach (var job in PlayableJobs(protos))
            {
                var role = protos.Index<RoleLoadoutPrototype>(LoadoutSystem.GetJobPrototype(job.ID));
                IEquipmentLoadout gear = protos.Index(job.StartingGear!.Value);

                foreach (var group in role.Groups.Where(g => g.Id.StartsWith("SWUniform") || g.Id.StartsWith("SWHead")))
                {
                    foreach (var item in protos.Index(group).Loadouts.Select(l => protos.Index(l)))
                    {
                        Assert.That(item.Effects.OfType<PointsCostLoadoutEffect>(), Is.Empty, $"{item.ID} costs points but only changes a look.");

                        foreach (var (slot, id) in item.Equipment)
                        {
                            var own = gear.GetGear(slot);
                            var ownProto = own == string.Empty ? null : protos.Index<EntityPrototype>(own);
                            var (armour, speed) = Stats(protos.Index(id), factory);
                            var (ownArmour, ownSpeed) = ownProto == null ? (null, null) : Stats(ownProto, factory);

                            Assert.That(speed, Is.EqualTo(ownSpeed), $"{item.ID} changes the speed of {job.ID}.");
                            Assert.That(armour, Is.EqualTo(ownArmour), $"{item.ID} changes the armour of {job.ID}.");
                        }
                    }
                }
            }
        });

        await pair.CleanReturnAsync();
    }

    private static (string? Armour, string? Speed) Stats(EntityPrototype proto, IComponentFactory factory)
    {
        string? armour = null;
        if (proto.TryGetComponent<ArmorComponent>(out var armorComp, factory))
        {
            armour = string.Join(",", armorComp.Modifiers.Coefficients.OrderBy(c => c.Key).Select(c => $"{c.Key}{c.Value}"))
                + string.Join(",", armorComp.Modifiers.FlatReduction.OrderBy(c => c.Key).Select(c => $"{c.Key}{c.Value}"));
        }

        string? speed = null;
        if (proto.TryGetComponent<ClothingSpeedModifierComponent>(out var speedComp, factory))
            speed = $"{speedComp.WalkModifier}/{speedComp.SprintModifier}";

        return (armour, speed);
    }

    /// <summary>
    /// A modified client sends a profile with what it wants. Play time decides, the server's own view of it.
    /// </summary>
    [Test]
    public async Task ServerRefusesWhatIsClosedUntilItOpens()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true, InLobby = true });
        var server = pair.Server;
        var prefs = server.ResolveDependency<IServerPreferencesManager>();
        var playTime = server.ResolveDependency<PlayTimeTrackingManager>();
        var inventory = server.System<InventorySystem>();
        var spawning = server.System<StationSpawningSystem>();
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var session = pair.Player!;
        var user = session.UserId;

        await server.WaitPost(() => server.CfgMan.SetCVar(CCVars.GameRoleLoadoutTimers, true));
        await pair.RunTicksSync(5);

        async Task Send(string item)
        {
            HumanoidCharacterProfile sent = null!;
            await server.WaitAssertion(() =>
            {
                var profile = (HumanoidCharacterProfile) prefs.GetPreferences(user).SelectedCharacter;
                var loadout = new RoleLoadout(SoldierRole);
                loadout.SetDefault(profile, session, server.ProtoMan, force: true);
                loadout.SelectedLoadouts[Weapons].Add(new Loadout { Prototype = item });
                sent = profile.WithLoadout(loadout);
            });

            Task task = null!;
            await server.WaitPost(() => task = prefs.SetProfile(user, prefs.GetPreferences(user).SelectedCharacterIndex, sent));
            for (var i = 0; i < 300 && !task.IsCompleted; i++)
            {
                await pair.RunTicksSync(1);
            }

            await task;
        }

        bool Selected(string item)
        {
            var profile = (HumanoidCharacterProfile) prefs.GetPreferences(user).SelectedCharacter;
            return profile.Loadouts.TryGetValue(SoldierRole, out var loadout)
                && loadout.SelectedLoadouts[Weapons].Any(l => l.Prototype == item);
        }

        // No hours: refused, and the job's own rifle is what spawns.
        await Send(Pistol);
        await server.WaitAssertion(() =>
        {
            Assert.That(Selected(Pistol), Is.False, "The server accepted a weapon the player has no hours for.");

            var profile = (HumanoidCharacterProfile) prefs.GetPreferences(user).SelectedCharacter;
            var mob = spawning.SpawnPlayerMob(map.GridCoords, Soldier, profile, null);
            Assert.That(inventory.TryGetSlotEntity(mob, "suitstorage", out var held));
            Assert.That(entMan.GetComponent<MetaDataComponent>(held!.Value).EntityPrototype?.ID, Is.EqualTo(Rifle));
            entMan.DeleteEntity(mob);
        });

        // A group the job does not have, and an item that is not in the group.
        await server.WaitPost(() =>
        {
            playTime.AddTimeToOverallPlaytime(session, TimeSpan.FromHours(3));
        });
        await pair.RunTicksSync(5);
        await Send("SWLoadoutWeaponZ1BlasterCarbine");
        await server.WaitAssertion(() => Assert.That(Selected("SWLoadoutWeaponZ1BlasterCarbine"), Is.False, "An item from another faction's group went through."));

        // Three hours open the pistol and not the heavy one.
        await Send(Pistol);
        await server.WaitAssertion(() =>
        {
            Assert.That(Selected(Pistol), Is.True, "The server dropped a weapon the player has the hours for.");
        });
        await Send(HeavyPistol);
        await server.WaitAssertion(() => Assert.That(Selected(HeavyPistol), Is.False));

        // Five hours open it, and it spawns in place of the rifle.
        await server.WaitPost(() => playTime.AddTimeToOverallPlaytime(session, TimeSpan.FromHours(3)));
        await pair.RunTicksSync(5);
        await Send(HeavyPistol);
        await server.WaitAssertion(() =>
        {
            Assert.That(Selected(HeavyPistol), Is.True);

            var profile = (HumanoidCharacterProfile) prefs.GetPreferences(user).SelectedCharacter;
            var mob = spawning.SpawnPlayerMob(map.GridCoords, Soldier, profile, null);
            Assert.That(inventory.TryGetSlotEntity(mob, "suitstorage", out var held));
            Assert.That(entMan.GetComponent<MetaDataComponent>(held!.Value).EntityPrototype?.ID, Is.EqualTo("SWM44"));
            entMan.DeleteEntity(mob);
        });

        await server.WaitPost(() => server.CfgMan.SetCVar(CCVars.GameRoleLoadoutTimers, false));
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The profile was fine when saved. A rule that changed since then is caught at spawn, and the group falls back.
    /// </summary>
    [Test]
    public async Task SpawnCheckDropsWhatStoppedBeingAllowed()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true, InLobby = true });
        var server = pair.Server;
        var prefs = server.ResolveDependency<IServerPreferencesManager>();
        var entMan = server.EntMan;
        var session = pair.Player!;

        await server.WaitPost(() => server.CfgMan.SetCVar(CCVars.GameRoleLoadoutTimers, true));
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var profile = (HumanoidCharacterProfile) prefs.GetPreferences(session.UserId).SelectedCharacter;

            // What the database could hold after a change of rules: no check has seen this yet.
            var role = new RoleLoadout(SoldierRole);
            role.SetDefault(profile, session, server.ProtoMan, force: true);
            role.SelectedLoadouts[Weapons].Add(new Loadout { Prototype = HeavyPistol });
            var mando = new RoleLoadout("JobSWMandalorianRecruit");
            mando.SetDefault(profile, session, server.ProtoMan, force: true);
            mando.SelectedLoadouts["SWMandoRecruit"].Clear();
            var stale = profile.WithLoadout(role).WithLoadout(mando);

            entMan.EventBus.RaiseEvent(EventSource.Local, new PlayerBeforeSpawnEvent(session, stale, Soldier, true, EntityUid.Invalid));
            Assert.That(stale.Loadouts[SoldierRole].SelectedLoadouts[Weapons], Is.Empty, "A closed weapon survived the spawn check.");

            // A required group gets its default back, not nothing.
            entMan.EventBus.RaiseEvent(EventSource.Local, new PlayerBeforeSpawnEvent(session, stale, "SWMandalorianRecruit", true, EntityUid.Invalid));
            Assert.That(stale.Loadouts["JobSWMandalorianRecruit"].SelectedLoadouts["SWMandoRecruit"].Select(l => l.Prototype.Id), Is.EqualTo(new[] { "SWMandRecruit1" }));
        });

        await server.WaitPost(() => server.CfgMan.SetCVar(CCVars.GameRoleLoadoutTimers, false));
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Species that refuse a slot get nothing there and find nothing at their feet.
    /// </summary>
    [Test]
    public async Task SpeciesWithoutASlotFindNothingOnTheFloor()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var spawning = server.System<StationSpawningSystem>();
        var inventory = server.System<InventorySystem>();
        var containers = server.System<SharedContainerSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var profile = HumanoidCharacterProfile.DefaultWithSpecies("Twilek");
            var mob = spawning.SpawnPlayerMob(map.GridCoords, Soldier, profile, null);

            Assert.That(inventory.TryGetSlotEntity(mob, "head", out _), Is.False);
            var fallen = new List<string>();
            var query = entMan.EntityQueryEnumerator<ItemComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.MapID == map.MapId && !containers.IsEntityInContainer(uid))
                    fallen.Add(entMan.ToPrettyString(uid).ToString());
            }

            Assert.That(fallen, Is.Empty);
            entMan.DeleteEntity(mob);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BudgetIsSpentToTheLastPoint()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protos = server.ProtoMan;

        await server.WaitAssertion(() =>
        {
            var collection = server.InstanceDependencyCollection;
            var profile = new HumanoidCharacterProfile();
            var loadout = new RoleLoadout(SoldierRole);
            loadout.SetDefault(profile, null, protos, force: true);
            loadout.AddLoadout("SWKit", "SWLoadoutKitBandage", protos);
            loadout.AddLoadout("SWKit", "SWLoadoutKitSmoke", protos);
            loadout.AddLoadout("SWSidearms", "SWLoadoutSidearmAnarchy", protos);
            loadout.EnsureValid(profile, pair.Player!, collection);

            Assert.That(protos.Index<RoleLoadoutPrototype>(SoldierRole).Points, Is.EqualTo(2));
            Assert.That(loadout.Points, Is.EqualTo(0), "A budget of 2 must buy two items of 1.");
            var chosen = loadout.SelectedLoadouts["SWKit"].Count + loadout.SelectedLoadouts["SWSidearms"].Count;
            Assert.That(chosen, Is.EqualTo(2), "The third item went over the budget.");
            Assert.That(loadout.IsValid(profile, pair.Player, "SWLoadoutKitKolto", collection, out var reason), Is.False);
            Assert.That(reason!.ToString(), Does.Contain("2"));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A claimed reward opens the same loadout the window and the spawn use. The window of the pass plays no part.
    /// </summary>
    [Test]
    public async Task BattlePassRewardOpensTheItem()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true, Connected = true });
        var server = pair.Server;
        var cfg = server.ResolveDependency<IConfigurationManager>();
        var season = server.ProtoMan.Index<SWBattlePassSeasonPrototype>("SWSeasonColdWar");
        var pass = server.System<SWBattlePassSystem>();
        var inventory = server.System<InventorySystem>();
        var spawning = server.System<StationSpawningSystem>();
        var collection = server.InstanceDependencyCollection;
        var map = await pair.CreateTestMap();
        var session = pair.Player!;
        const string reward = "SWCosmeticScarfWatch";
        const string group = "SWCosmeticsNeck";

        await server.WaitPost(() => cfg.SetCVar(SWVars.BattlePassNow, "2026-12-20"));
        await pair.RunTicksSync(5);

        bool Open()
        {
            var loadout = new RoleLoadout(SoldierRole);
            return loadout.IsValid(new HumanoidCharacterProfile(), session, reward, collection, out _);
        }

        await server.WaitAssertion(() =>
        {
            Assert.That(Open(), Is.False, "The reward was open before the claim.");

            var data = pass.GetData(session.UserId)!;
            data.Xp = 100000;
            Assert.That(pass.TryClaim(data, season, 1, SWBattlePassTrack.Free, out _), Is.True);
        });

        for (var i = 0; i < 300; i++)
        {
            await pair.RunTicksSync(1);
            var open = false;
            await server.WaitPost(() => open = Open());
            if (open)
                break;
        }

        await server.WaitAssertion(() =>
        {
            Assert.That(Open(), Is.True, "The claim did not open the item.");

            var profile = new HumanoidCharacterProfile();
            var role = server.ProtoMan.Index<RoleLoadoutPrototype>(SoldierRole);
            var loadout = Choose(role, server.ProtoMan.Index<LoadoutGroupPrototype>(group), server.ProtoMan.Index<LoadoutPrototype>(reward), profile, server.ProtoMan);
            var mob = spawning.SpawnPlayerMob(map.GridCoords, Soldier, profile.WithLoadout(loadout), null);
            Assert.That(inventory.TryGetSlotEntity(mob, "neck", out var worn));
            Assert.That(server.EntMan.GetComponent<MetaDataComponent>(worn!.Value).EntityPrototype?.ID, Is.EqualTo("SWCosmeticItemScarfWatch"));
            server.EntMan.DeleteEntity(mob);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Dying must not pay: what a body spawned with sells for nothing, the same item from a shop sells as before.
    /// </summary>
    [Test]
    public async Task SpawnedGearHasNoResaleValue()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var protos = server.ProtoMan;
        var pricing = server.System<PricingSystem>();
        var inventory = server.System<InventorySystem>();
        var spawning = server.System<StationSpawningSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var profile = new HumanoidCharacterProfile();
            var role = protos.Index<RoleLoadoutPrototype>(SoldierRole);
            var loadout = Choose(role, protos.Index<LoadoutGroupPrototype>("SWKit"), protos.Index<LoadoutPrototype>("SWLoadoutKitKolto"), profile, protos);
            var mob = spawning.SpawnPlayerMob(map.GridCoords, Soldier, profile.WithLoadout(loadout), null);

            Assert.That(inventory.TryGetSlotEntity(mob, "suitstorage", out var rifle));
            Assert.That(pricing.GetPrice(rifle!.Value), Is.Zero, "The job's own rifle sells for money.");

            var bought = entMan.SpawnEntity(Rifle, map.GridCoords);
            Assert.That(pricing.GetPrice(bought), Is.GreaterThan(0), "A rifle from a shop must still sell.");

            Assert.That(inventory.TryGetSlotEntity(mob, "back", out var bag));
            var storage = entMan.GetComponent<StorageComponent>(bag!.Value);
            var kolto = storage.Container.ContainedEntities.First(e => entMan.GetComponent<MetaDataComponent>(e).EntityPrototype?.ID == "SWKoltoMedipen");
            Assert.That(pricing.GetPrice(kolto), Is.Zero, "A kit item sells for money.");
            Assert.That(pricing.GetPrice(bag.Value), Is.Zero, "The bag it spawned with sells for money.");

            var armour = inventory.TryGetSlotEntity(mob, "outerClothing", out var suit);
            Assert.That(armour && pricing.GetPrice(suit!.Value) == 0, "The suit it spawned with sells for money.");

            // A bag keeps the price of what a player put in it after the spawn.
            var goods = entMan.SpawnEntity("SWKoltoMedipen", map.GridCoords);
            var goodsPrice = pricing.GetPrice(goods);
            Assert.That(goodsPrice, Is.GreaterThan(0));
            Assert.That(server.System<SharedStorageSystem>().Insert(bag.Value, goods, out _, storageComp: storage, playSound: false));
            Assert.That(pricing.GetPrice(bag.Value), Is.EqualTo(goodsPrice));

            entMan.DeleteEntity(mob);
            entMan.DeleteEntity(bought);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Two groups can name one slot: the later one is worn and the other is not even spawned.
    /// </summary>
    [Test]
    public async Task LaterChoiceTakesTheSlot()
    {
        await using var pair = await PoolManager.GetServerClient();
        var protos = pair.Server.ProtoMan;

        await pair.Server.WaitAssertion(() =>
        {
            var role = protos.Index<RoleLoadoutPrototype>(SoldierRole);
            var loadout = new RoleLoadout(SoldierRole);
            loadout.SetDefault(new HumanoidCharacterProfile(), null, protos, force: true);
            loadout.SelectedLoadouts["SWHeadRepublic"].Add(new Loadout { Prototype = "SWLoadoutCapRepublic" });
            loadout.SelectedLoadouts["SWNewYearRepublicSoldierHead"].Add(new Loadout { Prototype = "SWNewYearRepublicCap" });

            IEquipmentLoadout merged = SWMergedGear.Build(protos.Index<StartingGearPrototype>("RepublicSoldierGear"), loadout, role, protos);
            Assert.That(merged.GetGear("head"), Is.EqualTo("SWNewYearRepublicCap"));
        });

        await pair.CleanReturnAsync();
    }
}
