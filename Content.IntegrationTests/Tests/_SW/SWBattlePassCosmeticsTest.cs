#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Server.Cargo.Systems;
using Content.Shared._SW.BattlePass;
using Content.Shared._SW.Cosmetics;
using Content.Shared.Clothing.Components;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Inventory;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Roles;
using Content.Shared.Station;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._SW;

/// <summary>
/// Cosmetics change how a player looks and nothing else, and they must never knock job gear onto the floor.
/// </summary>
[TestFixture]
public sealed class SWBattlePassCosmeticsTest
{
    private const string NeckGroup = "SWCosmeticsNeck";
    private const string HeadGroup = "SWCosmeticsHead";

    /// <summary>
    /// Everything a cosmetic may have. Anything new on one of them, armour, storage, a speed modifier, a HUD, a price,
    /// fails this test, so an effect on play has to be argued for here.
    /// </summary>
    private static readonly HashSet<string> AllowedComponents =
    [
        "Clothing", "CargoSellBlacklist", "Sprite", "Item", "Appearance", "Animateable", "DamageExaminable", "GravityAffected",
        "Clickable", "InteractionOutline", "Physics", "Fixtures", "CollisionWake", "TileFrictionModifier",
        "MovedByPressure", "DamageOnHighSpeedImpact", "EmitSoundOnCollide", "EmitSoundOnLand", "EmitSoundOnPickup", "EmitSoundOnDrop",
        "Pullable", "Tag", "Transform", "MetaData", "Fingerprint", "DnaSubstanceTrace", "ResidueTrace", "Forensics",
        "CanBeThrown", "DamageOtherOnHit", "Thrown", "StaticPrice",
    ];

    private static IEnumerable<EntityPrototype> Cosmetics(IPrototypeManager proto)
    {
        return proto.EnumeratePrototypes<LoadoutPrototype>()
            .Where(l => l.Effects.OfType<SWCosmeticLoadoutEffect>().Any(e => !e.Seasonal))
            .SelectMany(l => l.Equipment.Values)
            .Select(id => proto.Index(id));
    }

    [Test]
    public async Task CosmeticsHaveNoEffectOnPlay()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var factory = server.ResolveDependency<IComponentFactory>();

        var cosmetics = Cosmetics(server.ProtoMan).ToList();
        Assert.That(cosmetics, Is.Not.Empty);

        await server.WaitAssertion(() =>
        {
            var pricing = server.System<PricingSystem>();
            Assert.Multiple(() =>
            {
                foreach (var proto in cosmetics)
                {
                    // A costume may protect, as the clothes it is made from do, and is held to them instead.
                    if (SWCostumeRule.IssuedParent(proto, server.ProtoMan) is { } parent)
                    {
                        SWCostumeRule.AssertAddsNothing(proto, parent, factory);
                        continue;
                    }

                    var uid = server.EntMan.SpawnEntity(proto.ID, map.GridCoords);

                    foreach (var comp in server.EntMan.GetComponents(uid))
                    {
                        var name = factory.GetComponentName(comp.GetType());
                        Assert.That(AllowedComponents, Does.Contain(name), $"{proto.ID} has {name}, which a cosmetic may not have");
                    }

                    // It comes free with every spawn, so it must sell for nothing.
                    Assert.That(pricing.GetPrice(uid), Is.EqualTo(0), $"{proto.ID} has a price");

                    var clothing = server.EntMan.GetComponent<ClothingComponent>(uid);
                    Assert.That(clothing.Slots == SlotFlags.NECK || clothing.Slots == SlotFlags.HEAD,
                        $"{proto.ID} goes in {clothing.Slots}: cosmetics are for the neck and the head only");

                    server.EntMan.DeleteEntity(uid);
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EveryCosmeticIsOwnedOnlyThroughTheEffect()
    {
        await using var pair = await PoolManager.GetServerClient();
        var proto = pair.Server.ProtoMan;

        Assert.Multiple(() =>
        {
            foreach (var groupId in new[] { NeckGroup, HeadGroup })
            {
                var group = proto.Index<LoadoutGroupPrototype>(groupId);
                Assert.That(group.MinLimit, Is.EqualTo(0), "nobody gets a locked item by default");
                Assert.That(group.MaxLimit, Is.EqualTo(1), "a second item would have no slot and fall on the floor");
            }

            // These groups also hold free things that a job may always wear. What the pass gives must stay locked.
            foreach (var season in proto.EnumeratePrototypes<SWBattlePassSeasonPrototype>())
            {
                var rewards = season.Tiers.SelectMany(t => new[] { t.Free?.Cosmetic, t.Paid?.Cosmetic });
                var fromCases = season.OvertimeCase is { } caseId
                    ? proto.Index(caseId).Entries.Select(e => e.Cosmetic)
                    : Enumerable.Empty<ProtoId<LoadoutPrototype>?>();

                foreach (var id in rewards.Concat(fromCases).OfType<ProtoId<LoadoutPrototype>>())
                {
                    Assert.That(proto.Index(id).Effects.OfType<SWCosmeticLoadoutEffect>().Any(), $"{id} can be selected without owning it");
                }
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EveryJobThatCanWearHasTheGroup()
    {
        await using var pair = await PoolManager.GetServerClient();
        var proto = pair.Server.ProtoMan;

        // Job gear that keeps its own neck item, as the design says.
        var ownNeck = new[] { "SWJedi", "SWSith", "SWMandalorianVeteran", "SWRepublicCommanderCommander" };

        Assert.Multiple(() =>
        {
            foreach (var job in proto.EnumeratePrototypes<JobPrototype>().Where(j => j.ID.StartsWith("SW") && j.SetPreference))
            {
                Assert.That(proto.TryIndex<RoleLoadoutPrototype>($"Job{job.ID}", out var role), $"{job.ID} has no loadout");
                if (role == null)
                    continue;

                Assert.That(role.Groups.Contains(NeckGroup), Is.EqualTo(!ownNeck.Contains(job.ID)), job.ID);
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The species lists on the loadouts must match what the bodies can really wear: a wrong one leaves the item on
    /// the floor, or locks it for nothing.
    /// </summary>
    [Test]
    public async Task BlockedSpeciesMatchTheirSlots()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var proto = server.ProtoMan;

        var groups = new (string Group, string Slot)[] { (NeckGroup, "neck"), (HeadGroup, "head") };

        await server.WaitAssertion(() =>
        {
            var inventory = server.System<InventorySystem>();
            Assert.Multiple(() =>
            {
                foreach (var species in proto.EnumeratePrototypes<SpeciesPrototype>().Where(s => s.RoundStart))
                {
                    foreach (var (groupId, slot) in groups)
                    {
                        var loadout = proto.Index(proto.Index<LoadoutGroupPrototype>(groupId).Loadouts
                            .First(id => proto.Index(id).Effects.OfType<SWCosmeticLoadoutEffect>().Any(e => !e.Seasonal)));
                        var effect = loadout.Effects.OfType<SWCosmeticLoadoutEffect>().Single();
                        var item = server.EntMan.SpawnEntity(((IEquipmentLoadout) loadout).GetGear(slot), map.GridCoords);
                        var mob = server.EntMan.SpawnEntity(species.Prototype, map.GridCoords);

                        var canWear = inventory.CanEquip(mob, item, slot, out _);
                        Assert.That(effect.BlockedSpecies.Contains(species.ID), Is.EqualTo(!canWear),
                            $"{species.ID} in {slot}: can wear {canWear}");

                        server.EntMan.DeleteEntity(mob);
                        server.EntMan.DeleteEntity(item);
                    }
                }
            });
        });

        await pair.CleanReturnAsync();
    }
}
