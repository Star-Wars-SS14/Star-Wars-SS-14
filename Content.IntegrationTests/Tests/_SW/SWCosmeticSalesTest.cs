using Content.Shared._SW.Loadouts;
using System.Collections.Generic;
using System.Linq;
using Content.Server._SW.Cosmetics;
using Content.Server.Atmos.Components;
using Content.Server.Cargo.Components;
using Content.Server.Cargo.Systems;
using Content.Server.Preferences.Managers;
using Content.Server.Station.Systems;
using Content.Server.Store.Components;
using Content.Server.Temperature.Components;
using Content.Shared._SW.Cosmetics;
using Content.Shared._SW.Overlays;
using Content.Shared.Armor;
using Content.Shared.Blocking;
using Content.Shared.CCVar;
using Content.Shared.Clothing;
using Content.Shared.Clothing.Components;
using Content.Shared.Containers;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage.Components;
using Content.Shared.Explosion.Components;
using Content.Shared.Eye.Blinding.Components;
using Content.Shared.Flash.Components;
using Content.Shared.GameTicking;
using Content.Shared.Humanoid;
using Content.Shared.Inventory;
using Content.Shared.Item;
using Content.Shared.Materials;
using Content.Shared.Nutrition.Components;
using Content.Shared.Overlays;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Roles;
using Content.Shared.Storage;
using Content.Shared.Storage.Components;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Reflect;
using Robust.Client.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._SW;

[TestFixture]
[TestOf(typeof(SWCosmeticSystem))]
public sealed class SWCosmeticSalesTest
{
    private const string Cosmetic = "SWCosmeticDrapeLeather";
    private const string CosmeticItem = "SWCosmeticDrapeLeather";
    private const string CosmeticGroup = "SWCosmeticsNeck";
    private const string Job = "SWRepublicSoldier";
    private const string Role = "JobSWRepublicSoldier";
    private const string NeckSlot = "neck";

    /// <summary>
    /// The only places a paid item may go, unless it is a costume made from what the job is issued.
    /// </summary>
    private static readonly string[] AllowedSlots = [NeckSlot, "head"];

    /// <summary>
    /// What a paid item must never have: protection, damage, storage, speed, a HUD, or a way to become money.
    /// </summary>
    private static readonly Type[] ForbiddenComponents =
    [
        typeof(ArmorComponent),
        typeof(ExplosionResistanceComponent),
        typeof(PressureProtectionComponent),
        typeof(TemperatureProtectionComponent),
        typeof(BlockingComponent),
        typeof(ReflectComponent),
        typeof(FlashImmunityComponent),
        typeof(EyeProtectionComponent),
        typeof(MeleeWeaponComponent),
        typeof(GunComponent),
        typeof(DamageOtherOnHitComponent),
        typeof(StorageComponent),
        typeof(StorageFillComponent),
        typeof(ContainerFillComponent),
        typeof(ItemSlotsComponent),
        typeof(ClothingSpeedModifierComponent),
        typeof(HeldSpeedModifierComponent),
        typeof(ShowJobIconsComponent),
        typeof(SWFactionJobHudComponent),
        typeof(ChameleonClothingComponent),
        typeof(StaticPriceComponent),
        typeof(CurrencyComponent),
        typeof(ButcherableComponent),
        typeof(PhysicalCompositionComponent),
    ];

    [Test]
    public async Task CosmeticsAreOnlyVisual()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protos = server.ProtoMan;
        var factory = server.ResolveDependency<IComponentFactory>();
        var pricing = server.System<PricingSystem>();

        await server.WaitAssertion(() =>
        {
            var cosmetics = protos.EnumeratePrototypes<LoadoutPrototype>().Where(IsCosmetic).ToList();
            Assert.That(cosmetics, Is.Not.Empty);

            foreach (var loadout in cosmetics)
            {
                Assert.That(loadout.StartingGear, Is.Null, $"{loadout.ID} must list its item directly.");
                Assert.That(loadout.Inhand, Is.Empty, $"{loadout.ID} puts something in the hands.");
                Assert.That(loadout.Storage, Is.Empty, $"{loadout.ID} adds items to storage.");

                Assert.That(loadout.Equipment, Is.Not.Empty, $"{loadout.ID} gives nothing.");

                foreach (var (slot, item) in loadout.Equipment)
                {
                    var proto = protos.Index(item);

                    // The one exception: the same clothes in another look, worth exactly as much in every job.
                    if (SWCostumeRule.IssuedParent(proto, protos) is { } parent)
                    {
                        SWCostumeRule.AssertAddsNothing(proto, parent, factory);
                        SWCostumeRule.AssertSameForEveryJob(loadout, slot, proto, protos, factory);
                        Assert.That(pricing.GetEstimatedPrice(proto), Is.EqualTo(pricing.GetEstimatedPrice(parent)), $"{item} sells for more than {parent.ID}.");
                        continue;
                    }

                    Assert.That(AllowedSlots, Does.Contain(slot), $"{loadout.ID} uses slot {slot}.");

                    foreach (var forbidden in ForbiddenComponents)
                    {
                        Assert.That(proto.Components.ContainsKey(factory.GetComponentName(forbidden)),
                            Is.False,
                            $"{item} has {forbidden.Name}.");
                    }

                    Assert.That(pricing.GetEstimatedPrice(proto), Is.Zero, $"{item} can be sold for credits.");
                }

                foreach (var group in protos.EnumeratePrototypes<LoadoutGroupPrototype>())
                {
                    if (group.Loadouts.Contains(loadout.ID))
                        Assert.That(group.MinLimit, Is.Zero, $"{group.ID} forces a paid item on someone.");
                }
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A cosmetic must take the place of what the job would wear there, and leave nothing on the floor. Compares a real
    /// spawn with and without the cosmetic, for every job that offers one.
    /// </summary>
    [Test]
    public async Task GearDoesNotFallToTheFloor()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var protos = server.ProtoMan;
        var spawning = server.System<StationSpawningSystem>();
        var inventory = server.System<InventorySystem>();
        var containers = server.System<SharedContainerSystem>();
        var map = await pair.CreateTestMap();

        int CountOnFloor()
        {
            var count = 0;
            var query = entMan.EntityQueryEnumerator<ItemComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.MapID == map.MapId && !containers.IsEntityInContainer(uid))
                    count++;
            }

            return count;
        }

        void ClearFloor()
        {
            var query = entMan.EntityQueryEnumerator<ItemComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.MapID == map.MapId)
                    entMan.QueueDeleteEntity(uid);
            }
        }

        EntityUid Spawn(JobPrototype job, RoleLoadoutPrototype role, RoleLoadout loadout)
        {
            var mob = entMan.SpawnEntity("MobHuman", map.GridCoords);
            spawning.EquipStartingGear(mob, SWMergedGear.Build(protos.Index(job.StartingGear!.Value), loadout, role, protos), raiseEvent: false);
            return mob;
        }

        var checkedJobs = 0;
        var jobs = protos.EnumeratePrototypes<JobPrototype>()
            .Where(j => j.StartingGear != null && protos.HasIndex<RoleLoadoutPrototype>(LoadoutSystem.GetJobPrototype(j.ID)))
            .ToList();

        foreach (var job in jobs)
        {
            var role = protos.Index<RoleLoadoutPrototype>(LoadoutSystem.GetJobPrototype(job.ID));
            foreach (var groupId in role.Groups)
            {
                var group = protos.Index(groupId);
                foreach (var loadoutId in group.Loadouts)
                {
                    var cosmetic = protos.Index(loadoutId);
                    if (!IsCosmetic(cosmetic))
                        continue;

                    checkedJobs++;
                    await server.WaitAssertion(() =>
                    {
                        var clean = new RoleLoadout(role.ID);
                        clean.SetDefault(new HumanoidCharacterProfile(), null, protos, force: true);
                        var mob = Spawn(job, role, clean);
                        var expected = CountOnFloor();
                        entMan.DeleteEntity(mob);
                        ClearFloor();

                        var worn = new RoleLoadout(role.ID);
                        worn.SetDefault(new HumanoidCharacterProfile(), null, protos, force: true);
                        worn.SelectedLoadouts[group.ID].Add(new Loadout { Prototype = cosmetic.ID });
                        mob = Spawn(job, role, worn);

                        Assert.That(CountOnFloor(), Is.EqualTo(expected), $"{job.ID} with {cosmetic.ID} dropped gear.");
                        foreach (var (slot, item) in cosmetic.Equipment)
                        {
                            Assert.That(inventory.TryGetSlotEntity(mob, slot, out var wornItem), $"{job.ID} does not wear {item}.");
                            Assert.That(entMan.GetComponent<MetaDataComponent>(wornItem!.Value).EntityPrototype?.ID, Is.EqualTo(item.Id));
                        }

                        entMan.DeleteEntity(mob);
                        ClearFloor();
                    });

                    await pair.RunTicksSync(1);
                }
            }
        }

        Assert.That(checkedJobs, Is.GreaterThan(0));
        await pair.CleanReturnAsync();
    }

    [Test]
    public void SeasonDates()
    {
        Assert.Multiple(() =>
        {
            // Crosses the new year.
            Assert.That(SWCosmeticSeason.InYearlyWindow(new DateTime(2026, 12, 1), "12-01", "02-29"), Is.True);
            Assert.That(SWCosmeticSeason.InYearlyWindow(new DateTime(2027, 1, 15), "12-01", "02-29"), Is.True);
            Assert.That(SWCosmeticSeason.InYearlyWindow(new DateTime(2027, 2, 28), "12-01", "02-29"), Is.True);
            Assert.That(SWCosmeticSeason.InYearlyWindow(new DateTime(2028, 2, 29), "12-01", "02-29"), Is.True);
            Assert.That(SWCosmeticSeason.InYearlyWindow(new DateTime(2027, 3, 1), "12-01", "02-29"), Is.False);
            Assert.That(SWCosmeticSeason.InYearlyWindow(new DateTime(2026, 11, 30), "12-01", "02-29"), Is.False);

            // Stays inside one year.
            Assert.That(SWCosmeticSeason.InYearlyWindow(new DateTime(2026, 6, 15), "06-01", "06-30"), Is.True);
            Assert.That(SWCosmeticSeason.InYearlyWindow(new DateTime(2026, 7, 1), "06-01", "06-30"), Is.False);

            Assert.That(SWCosmeticSeason.InDateWindow(new DateTime(2026, 12, 15), "2026-12-15", "2027-01-15"), Is.True);
            Assert.That(SWCosmeticSeason.InDateWindow(new DateTime(2027, 1, 15), "2026-12-15", "2027-01-15"), Is.True);
            Assert.That(SWCosmeticSeason.InDateWindow(new DateTime(2027, 1, 16), "2026-12-15", "2027-01-15"), Is.False);
            Assert.That(SWCosmeticSeason.InDateWindow(new DateTime(2026, 12, 14), "2026-12-15", "2027-01-15"), Is.False);
            Assert.That(SWCosmeticSeason.InDateWindow(new DateTime(2027, 12, 20), "2026-12-15", "2027-01-15"), Is.False);

            // A typo closes the window instead of opening it.
            Assert.That(SWCosmeticSeason.InYearlyWindow(new DateTime(2026, 12, 20), "december", "02-29"), Is.False);
            Assert.That(SWCosmeticSeason.InDateWindow(new DateTime(2026, 12, 20), "2026-12-15", "soon"), Is.False);

            Assert.That(SWCosmeticSeason.FormatDay("12-01"), Is.EqualTo("01.12"));
        });
    }

    /// <summary>
    /// The whole way of an item: refused, granted, kept, off season, revoked. The selection is sent straight to the
    /// server the way a modified client would send it, and the UI plays no part.
    /// </summary>
    [Test]
    public async Task OwnershipDecidesWhatTheServerAccepts()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Connected = true,
            InLobby = true,
            Dirty = true,
        });
        var server = pair.Server;
        var client = pair.Client;
        var prefs = server.ResolveDependency<IServerPreferencesManager>();
        var cosmetics = server.System<SWCosmeticSystem>();
        var spawning = server.System<StationSpawningSystem>();
        var inventory = server.System<InventorySystem>();
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitPost(() => server.CfgMan.SetCVar(SWVars.CosmeticSeason, SWCosmeticSeason.ModeOn));
        await pair.RunTicksSync(5);

        var session = pair.Player!;
        var user = session.UserId;

        async Task WaitFor(Func<Task> action)
        {
            Task task = null!;
            await server.WaitPost(() => task = action());
            for (var i = 0; i < 300 && !task.IsCompleted; i++)
            {
                await pair.RunTicksSync(1);
            }

            await task;
        }

        async Task<List<string>> Owned()
        {
            List<string> owned = null!;
            await WaitFor(async () => owned = await cosmetics.GetOwned(user));
            return owned;
        }

        async Task SetOwned(string item, bool owned)
        {
            await WaitFor(() => cosmetics.SetOwned(user, session.Name, item, owned, "test"));
            await pair.RunTicksSync(5);
        }

        // What a modified client sends: a profile with the item selected, whatever the UI allowed.
        async Task SendSelection()
        {
            HumanoidCharacterProfile sent = null!;
            await server.WaitAssertion(() =>
            {
                // A new player's character has a random species, and a colicoid cannot wear this item at all.
                var profile = HumanoidCharacterProfile.DefaultWithSpecies("Human");
                var loadout = new RoleLoadout(Role);
                loadout.SetDefault(profile, session, server.ProtoMan, force: true);
                loadout.SelectedLoadouts[CosmeticGroup].Add(new Loadout { Prototype = Cosmetic });
                // The first character's species is random, and this item is closed to one of them.
                sent = profile.WithSpecies(SharedHumanoidAppearanceSystem.DefaultSpecies).WithLoadout(loadout);
            });
            await WaitFor(() => prefs.SetProfile(user, prefs.GetPreferences(user).SelectedCharacterIndex, sent));
        }

        bool Selected()
        {
            var profile = (HumanoidCharacterProfile) prefs.GetPreferences(user).SelectedCharacter;
            return profile.Loadouts.TryGetValue(Role, out var loadout)
                && loadout.SelectedLoadouts.TryGetValue(CosmeticGroup, out var group)
                && group.Any(l => l.Prototype == Cosmetic);
        }

        bool ClientMayPick(out string? reason)
        {
            var collection = client.InstanceDependencyCollection;
            var local = client.ResolveDependency<IPlayerManager>().LocalSession!;
            var loadout = new RoleLoadout(Role);
            var valid = loadout.IsValid(new HumanoidCharacterProfile(), local, Cosmetic, collection, out var message);
            reason = message?.ToString();
            return valid;
        }

        // Not owned: the server drops the selection and the client is told how to get it.
        await SendSelection();
        await server.WaitAssertion(() => Assert.That(Selected(), Is.False, "The server accepted an item nobody granted."));
        await client.WaitAssertion(() =>
        {
            Assert.That(ClientMayPick(out var reason), Is.False);
            Assert.That(reason, Is.AnyOf(
                Loc.GetString("sw-cosmetic-not-owned"),
                Loc.GetString("sw-cosmetic-sale-closed")));
        });

        // Granted: kept by the server, known to the client, worn on spawn.
        await SetOwned(Cosmetic, true);
        Assert.That(await Owned(), Does.Contain(Cosmetic));
        await pair.RunTicksSync(5);
        await client.WaitAssertion(() => Assert.That(ClientMayPick(out _), Is.True));

        await SendSelection();
        await server.WaitAssertion(() =>
        {
            Assert.That(Selected(), Is.True, "The server dropped an item the player owns.");

            var profile = (HumanoidCharacterProfile) prefs.GetPreferences(user).SelectedCharacter;
            entMan.EventBus.RaiseEvent(EventSource.Local, new PlayerBeforeSpawnEvent(session, profile, Job, true, EntityUid.Invalid));
            Assert.That(Selected(), Is.True, "The spawn check dropped an item the player owns.");

            var mob = spawning.SpawnPlayerMob(map.GridCoords, Job, profile, null);
            Assert.That(inventory.TryGetSlotEntity(mob, NeckSlot, out var worn));
            Assert.That(entMan.GetComponent<MetaDataComponent>(worn!.Value).EntityPrototype?.ID, Is.EqualTo(CosmeticItem));
            entMan.DeleteEntity(mob);
        });

        // Off season: ownership stays, the item is not worn, the spawn check catches a selection made earlier.
        await server.WaitPost(() => server.CfgMan.SetCVar(SWVars.CosmeticSeason, SWCosmeticSeason.ModeOff));
        await pair.RunTicksSync(5);
        await client.WaitAssertion(() =>
        {
            Assert.That(ClientMayPick(out var reason), Is.False);
            Assert.That(reason, Is.EqualTo(Loc.GetString("sw-cosmetic-owned-off-season", ("date", "01.12"))));
        });
        await server.WaitAssertion(() =>
        {
            var profile = (HumanoidCharacterProfile) prefs.GetPreferences(user).SelectedCharacter;
            Assert.That(Selected(), Is.True);
            entMan.EventBus.RaiseEvent(EventSource.Local, new PlayerBeforeSpawnEvent(session, profile, Job, true, EntityUid.Invalid));
            Assert.That(Selected(), Is.False, "The spawn check let a winter item through off season.");

            var mob = spawning.SpawnPlayerMob(map.GridCoords, Job, profile, null);
            Assert.That(inventory.TryGetSlotEntity(mob, NeckSlot, out _), Is.False);
            entMan.DeleteEntity(mob);
        });
        Assert.That(await Owned(), Does.Contain(Cosmetic), "The season took away a paid item.");

        // Back in season, then revoked: the server drops it again.
        await server.WaitPost(() => server.CfgMan.SetCVar(SWVars.CosmeticSeason, SWCosmeticSeason.ModeOn));
        await pair.RunTicksSync(5);
        await SendSelection();
        await server.WaitAssertion(() => Assert.That(Selected(), Is.True));

        await SetOwned(Cosmetic, false);
        Assert.That(await Owned(), Does.Not.Contain(Cosmetic));
        await server.WaitAssertion(() =>
        {
            var profile = (HumanoidCharacterProfile) prefs.GetPreferences(user).SelectedCharacter;
            entMan.EventBus.RaiseEvent(EventSource.Local, new PlayerBeforeSpawnEvent(session, profile, Job, true, EntityUid.Invalid));
            Assert.That(Selected(), Is.False, "The spawn check let a revoked item through.");
        });
        await SendSelection();
        await server.WaitAssertion(() => Assert.That(Selected(), Is.False));
        await pair.RunTicksSync(5);
        await client.WaitAssertion(() => Assert.That(ClientMayPick(out _), Is.False));

        await pair.CleanReturnAsync();
    }

    private static bool IsCosmetic(LoadoutPrototype loadout)
    {
        return loadout.Effects.Any(e => e is SWCosmeticLoadoutEffect);
    }
}
