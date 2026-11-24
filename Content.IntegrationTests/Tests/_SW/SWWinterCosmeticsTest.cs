#nullable enable
using Content.Shared._SW.Loadouts;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Gravity;
using Content.Server.Station.Systems;
using Content.Shared.Armor;
using Content.Shared.Atmos;
using Content.Shared.Clothing;
using Content.Shared.Damage.Components;
using Content.Shared.Gravity;
using Content.Shared.Inventory;
using Content.Shared.Roles;
using Content.Shared.Storage;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Melee;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._SW;

/// <summary>
/// Cosmetics must give no advantage. Starting gear is equipped with force off, after any loadout, so a cosmetic in a
/// slot the job's gear also uses would leave that gear on the floor: these tests pin down which gear that is.
/// </summary>
[TestFixture]
public sealed class SWWinterCosmeticsTest
{
    private const string Hat = "SWCosmeticCapMechanic";
    private const string Snowball = "SWWinterSnowball";

    private static readonly string[] NeckItems =
    [
        "SWCosmeticDrapeLeather",
        "SWCosmeticDrapeSand",
        "SWClothingNeckWinterScarf",
        "SWClothingNeckBandana",
        "SWNewYearCloakRed",
        "SWNewYearCloakBlue",
        "SWNewYearCloakGreen",
        "SWWinterCloakFur",
        "SWWinterCloakPlaid",
        "SWWinterCloakWool",
        "SWWinterCloakFrost",
        "SWWinterCloakLeather",
        "SWWinterCloakDark",
    ];

    private static readonly string[] Coats =
    [
        "SWCosmeticCoatAsh",
        "SWCosmeticCoatSand",
        "SWNewYearCourierSuit",
        "SWNewYearSnowmanSuit",
        "SWNewYearCourierCap",
        "SWNewYearSnowmanHead",
    ];

    /// <summary>
    /// Job gear that already fills the neck: a neck cosmetic must not be given to these jobs.
    /// </summary>
    private static readonly HashSet<string> NeckTakenGear =
    [
        "ForceUserSithGear",
        "ForceUserJediGear",
        "MandalorianVeteranGear",
        "RepublicCommanderCommanderGear",
        "SithGear",
        "JediGear",
    ];

    /// <summary>
    /// The only gear the hat is meant for: these jobs have no helmet in their gear and no helmet from a loadout.
    /// </summary>
    private static readonly string[] HatGear =
    [
        "CitizenGear",
        "SobrikOfficerGear",
        "SobrikMedicGear",
    ];

    [Test]
    public async Task CosmeticsGiveNoAdvantage()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var factory = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            foreach (var id in NeckItems.Concat(Coats).Concat(new[] { Hat, "SWClothingHeadWinterHat", "SWClothingEyesDustGoggles", Snowball }))
            {
                var proto = protoMan.Index<EntityPrototype>(id);
                Assert.Multiple(() =>
                {
                    Assert.That(proto.TryGetComponent<ArmorComponent>(out _, factory), Is.False, $"{id} has armour.");
                    Assert.That(proto.TryGetComponent<StorageComponent>(out _, factory), Is.False, $"{id} has storage.");
                    Assert.That(proto.TryGetComponent<MeleeWeaponComponent>(out _, factory), Is.False, $"{id} is a melee weapon.");
                    Assert.That(proto.TryGetComponent<DamageOtherOnHitComponent>(out _, factory), Is.False, $"{id} hurts when thrown.");
                    Assert.That(proto.TryGetComponent<ClothingSpeedModifierComponent>(out _, factory), Is.False, $"{id} changes speed.");
                    Assert.That(proto.Components.ContainsKey("IdentityBlocker"), Is.False, $"{id} hides the wearer's identity.");
                    Assert.That(proto.Components.ContainsKey("PressureProtection"), Is.False, $"{id} protects from pressure.");
                    Assert.That(proto.Components.ContainsKey("TemperatureProtection"), Is.False, $"{id} protects from temperature.");
                    Assert.That(proto.Components.ContainsKey("SWFactionJobHud"), Is.False, $"{id} shows faction icons.");
                    Assert.That(proto.Components.ContainsKey("HideLayerClothing"), Is.False, $"{id} hides body layers.");
                });
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A loadout goes on before the job's gear, and the gear then skips that slot, so a neck cosmetic would take a job's cloak away.
    /// </summary>
    [Test]
    public async Task NeckTakenGearIsListed()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protoMan = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            foreach (var gear in protoMan.EnumeratePrototypes<JobPrototype>()
                         .Where(job => job.ID.StartsWith("SW") && job.StartingGear != null)
                         .Select(job => job.StartingGear!.Value.Id)
                         .Distinct())
            {
                var fillsNeck = !string.IsNullOrEmpty(Gear(protoMan, gear, "neck"));
                Assert.That(fillsNeck, Is.EqualTo(NeckTakenGear.Contains(gear)), $"Update the list for {gear}.");
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PlainHatIsForGearWithoutAHelmet()
    {
        await using var pair = await PoolManager.GetServerClient();
        var protoMan = pair.Server.ResolveDependency<IPrototypeManager>();

        await pair.Server.WaitAssertion(() =>
        {
            foreach (var gear in HatGear)
                Assert.That(Gear(protoMan, gear, "head"), Is.Null.Or.Empty, $"{gear} has a helmet.");

            // Why the plain hat must not go to soldiers: it would take the place of the helmet, with its faction icons and sealing.
            Assert.That(Gear(protoMan, "EmpireSoldierGear", "head"), Is.Not.Null.And.Not.Empty);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A costume from a job's loadout replaces the job's own item: nothing on the floor, and the same armour and job icons.
    /// </summary>
    [TestCase("EmpireSoldierGear", "outerClothing", "SWNewYearImperialSuit", "SWImperialSuit")]
    [TestCase("EmpireSoldierGear", "head", "SWNewYearImperialCap", "SW_ClothingHeadImperialHelmet")]
    [TestCase("RepublicSoldierGear", "outerClothing", "SWNewYearRepublicSuit", "SWRepublicSuit")]
    [TestCase("RepublicSoldierGear", "head", "SWNewYearRepublicCap", "SW_ClothingHeadRepublicHelmet")]
    [TestCase("CitizenGear", "outerClothing", "SWNewYearCourierSuit", "SWCitizenClothing")]
    [TestCase("CitizenGear", "outerClothing", "SWNewYearSnowmanSuit", "SWCitizenClothing")]
    [TestCase("CitizenGear", "outerClothing", "SWNewYearDedMorozSuit", "SWCitizenClothing")]
    [TestCase("CitizenGear", "outerClothing", "SWNewYearSweater", "SWCitizenClothing")]
    [TestCase("RepublicSoldierGear", "outerClothing", "SWWinterRepublicGarrisonSuit", "SWRepublicSuit")]
    [TestCase("RepublicSoldierGear", "outerClothing", "SWWinterRepublicColdWarSuit", "SWRepublicSuit")]
    [TestCase("RepublicSoldierGear", "outerClothing", "SWWinterRepublicVeteranSuit", "SWRepublicSuit")]
    [TestCase("RepublicSoldierGear", "head", "SWWinterRepublicSnowHelmet", "SW_ClothingHeadRepublicHelmet")]
    [TestCase("EmpireSoldierGear", "outerClothing", "SWWinterImperialGarrisonSuit", "SWImperialSuit")]
    [TestCase("EmpireSoldierGear", "outerClothing", "SWWinterImperialColdWarSuit", "SWImperialSuit")]
    [TestCase("EmpireSoldierGear", "outerClothing", "SWWinterImperialVeteranSuit", "SWImperialSuit")]
    [TestCase("EmpireSoldierGear", "head", "SWWinterImperialSnowHelmet", "SW_ClothingHeadImperialHelmet")]
    [TestCase("RepublicSoldierGear", "outerClothing", "SWWinterRepublicParkaSuit", "SWRepublicSuit")]
    [TestCase("RepublicSoldierGear", "outerClothing", "SWWinterRepublicFrostSuit", "SWRepublicSuit")]
    [TestCase("RepublicSoldierGear", "outerClothing", "SWWinterRepublicPonchoSuit", "SWRepublicSuit")]
    [TestCase("EmpireSoldierGear", "outerClothing", "SWWinterImperialParkaSuit", "SWImperialSuit")]
    [TestCase("EmpireSoldierGear", "outerClothing", "SWWinterImperialFrostSuit", "SWImperialSuit")]
    [TestCase("EmpireSoldierGear", "outerClothing", "SWWinterImperialPonchoSuit", "SWImperialSuit")]
    [TestCase("RepublicEngineerGear", "outerClothing", "SWWinterRepublicWorkCoat", "SWRepublicSuit")]
    [TestCase("EmpireEngineerGear", "outerClothing", "SWWinterImperialWorkCoat", "SWImperialSuit")]
    [TestCase("RepublicPilotGear", "outerClothing", "SWWinterRepublicFlightCoat", "SWPilotRepublicSuit")]
    [TestCase("EmpirePilotGear", "outerClothing", "SWWinterImperialFlightCoat", "SWPilotImperialSuit")]
    [TestCase("RepublicLieutenantGear", "outerClothing", "SWWinterRepublicGreatcoat", "SWLieutenantRepublicSuit")]
    [TestCase("EmpireLieutenantGear", "outerClothing", "SWWinterImperialGreatcoat", "SWLieutenantImperialSuit")]
    [TestCase("RepublicSoldierGear", "head", "SWWinterRepublicCoverHelmet", "SW_ClothingHeadRepublicHelmet")]
    [TestCase("EmpireSoldierGear", "head", "SWWinterImperialCoverHelmet", "SW_ClothingHeadImperialHelmet")]
    [TestCase("EmpireLieutenantGear", "outerClothing", "SWWinterImperialOfficerSuit", "SWLieutenantImperialSuit")]
    [TestCase("RepublicLieutenantGear", "outerClothing", "SWWinterRepublicOfficerSuit", "SWLieutenantRepublicSuit")]
    [TestCase("BlackSunMercenaryGear", "outerClothing", "SWWinterBlackSunFurSuit", "SWBlackSunArmor")]
    public async Task CostumeReplacesTheJobsItem(string gear, string slot, string costume, string regular)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var factory = server.ResolveDependency<IComponentFactory>();
        var spawning = entMan.System<StationSpawningSystem>();
        var inventory = entMan.System<InventorySystem>();
        var containers = entMan.System<SharedContainerSystem>();

        await server.WaitAssertion(() =>
        {
            Assert.That(Gear(protoMan, gear, slot), Is.EqualTo(regular), "The job no longer wears the item this costume is a child of.");
            var loose = LooseGear(entMan, protoMan, spawning, inventory, containers, testMap.GridCoords, gear, costume, slot);
            Assert.That(loose, Is.Empty, $"{costume} left {string.Join(", ", loose)} on the floor.");

            var mob = entMan.SpawnEntity("MobHuman", testMap.GridCoords);
            var merged = SWMergedGear.Build(protoMan.Index<StartingGearPrototype>(gear), null, null, protoMan);
            merged.Equipment[slot] = costume;
            spawning.EquipStartingGear(mob, merged, raiseEvent: false);
            inventory.TryGetSlotEntity(mob, slot, out var worn);
            Assert.That(entMan.GetComponent<MetaDataComponent>(worn!.Value).EntityPrototype!.ID, Is.EqualTo(costume));
            entMan.DeleteEntity(mob);

            var a = protoMan.Index<EntityPrototype>(costume);
            var b = protoMan.Index<EntityPrototype>(regular);
            Assert.That(a.TryGetComponent<ArmorComponent>(out var armourA, factory), Is.EqualTo(b.TryGetComponent<ArmorComponent>(out var armourB, factory)));
            if (armourA != null && armourB != null)
                Assert.That(armourA.Modifiers.Coefficients, Is.EquivalentTo(armourB.Modifiers.Coefficients));
            Assert.That(a.Components.ContainsKey("SWFactionJobHud"), Is.EqualTo(b.Components.ContainsKey("SWFactionJobHud")), "Job icons differ.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SnowballHurtsNoOneAndVanishesOnLanding()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var throwing = entMan.System<ThrowingSystem>();

        EntityUid snowball = default;
        EntityUid target = default;
        await server.WaitPost(() =>
        {
            // Without air the target takes damage on its own, and that would pass for the snowball's.
            var air = new GasMixture(Atmospherics.CellVolume) { Temperature = Atmospherics.T20C };
            air.AdjustMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
            air.AdjustMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);
            entMan.System<AtmosphereSystem>().SetMapAtmosphere(testMap.MapUid, false, air);

            // A test map has no gravity and its grid is one tile, so the snowball flies off the grid. A weightless item never lands.
            foreach (var place in new[] { testMap.MapUid, testMap.Grid.Owner })
            {
                var gravity = entMan.EnsureComponent<GravityComponent>(place);
                entMan.System<GravitySystem>().EnableGravity(place, gravity);
            }

            target = entMan.SpawnEntity("MobHuman", testMap.GridCoords.Offset(new Vector2(2, 0)));
            var thrower = entMan.SpawnEntity("MobHuman", testMap.GridCoords.Offset(new Vector2(0, 0)));
            snowball = entMan.SpawnEntity(Snowball, testMap.GridCoords.Offset(new Vector2(0.5f, 0)));
            throwing.TryThrow(snowball, new Vector2(4, 0), 8f, thrower, pushbackRatio: 0f);
        });

        await pair.RunTicksSync(120);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(snowball), "The snowball did not vanish after landing.");
            Assert.That(entMan.GetComponent<DamageableComponent>(target).TotalDamage.Float(), Is.EqualTo(0f), "The snowball hurt its target.");
        });

        await pair.CleanReturnAsync();
    }

    private static string? Gear(IPrototypeManager protoMan, string gear, string slot)
    {
        return ((IEquipmentLoadout) protoMan.Index<StartingGearPrototype>(gear)).GetGear(slot);
    }

    /// <summary>
    /// Prototype IDs of items that land outside any container when gear is equipped over a cosmetic, and not without it.
    /// </summary>
    private static List<string> LooseGear(
        IEntityManager entMan,
        IPrototypeManager protoMan,
        StationSpawningSystem spawning,
        InventorySystem inventory,
        SharedContainerSystem containers,
        Robust.Shared.Map.EntityCoordinates coords,
        string gear,
        string cosmetic,
        string slot)
    {
        var baseline = Loose(gear, null);
        var result = Loose(gear, cosmetic);
        foreach (var id in baseline)
            result.Remove(id);
        return result;

        List<string> Loose(string gearId, string? worn)
        {
            var before = new HashSet<EntityUid>();
            var query = entMan.AllEntityQueryEnumerator<MetaDataComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out _, out _))
                before.Add(uid);

            var mob = entMan.SpawnEntity("MobHuman", coords);
            var merged = SWMergedGear.Build(protoMan.Index<StartingGearPrototype>(gearId), null, null, protoMan);
            if (worn != null)
                merged.Equipment[slot] = worn;

            spawning.EquipStartingGear(mob, merged, raiseEvent: false);

            var fallen = new List<string>();
            var spawned = new List<EntityUid> { mob };
            var after = entMan.AllEntityQueryEnumerator<MetaDataComponent, TransformComponent>();
            while (after.MoveNext(out var uid, out var meta, out _))
            {
                if (before.Contains(uid) || uid == mob)
                    continue;
                spawned.Add(uid);
                if (!containers.IsEntityOrParentInContainer(uid) && meta.EntityPrototype != null && !entMan.HasComponent<Robust.Shared.Map.Components.MapGridComponent>(uid))
                    fallen.Add(meta.EntityPrototype.ID);
            }

            foreach (var uid in spawned)
            {
                if (!entMan.Deleted(uid))
                    entMan.DeleteEntity(uid);
            }

            return fallen;
        }
    }
}
