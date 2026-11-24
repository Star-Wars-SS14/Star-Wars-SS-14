#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Hands;
using Content.Shared.Item;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._SW;

/// <summary>
/// A gun held in one hand takes a magazine from the other hand, the way a player reloads:
/// the magazine is in the active hand and the player clicks the hand that holds the gun.
/// </summary>
public sealed class SWGunReloadInHandTest : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman"; // The default test mob has only one hand.

    private const string MagazineSlot = "gun_magazine";

    [Test]
    [TestCase("WeaponRifleLecter")] // Stock gun, for parity.
    [TestCase("SWE1BlasterRifle")]
    [TestCase("SWMK3BlasterRifle")]
    [TestCase("SWEL34")] // Heavy gun: it used to take both hands for good.
    public async Task ReloadHeldGun(string gun)
    {
        var failure = await TryReloadInHand(gun);
        Assert.That(failure, Is.Null, failure);
    }

    /// <summary>
    /// The heavy guns gave up MultiHandedItem for Wieldable: they still fire only with both hands on them.
    /// </summary>
    [Test]
    [TestCase("SWEL34")]
    [TestCase("SWGrantekF11D2")]
    [TestCase("SWGT-13ST")]
    [TestCase("SWGallantry")]
    [TestCase("SWMK1")]
    public async Task HeavyGunFiresOnlyWielded(string gun)
    {
        await Server.WaitPost(() =>
        {
            foreach (var hand in Hands!.SortedHands)
            {
                if (HandSys.GetHeldItem((SPlayer, Hands), hand) is { } held)
                    SEntMan.DeleteEntity(held);
            }
        });
        await RunTicks(1);

        var gunNet = await PlaceInHands(gun);
        Assert.That(HasComp<GunRequiresWieldComponent>(gunNet), $"{gun} must not fire from one hand");

        var wieldable = SEntMan.System<SharedWieldableSystem>();
        await Server.WaitPost(() =>
        {
            var uid = ToServer(gunNet);
            Assert.That(wieldable.TryWield(uid, SEntMan.GetComponent<WieldableComponent>(uid), SPlayer));
        });
        await RunTicks(5);

        Assert.That(Comp<WieldableComponent>(gunNet).Wielded, $"{gun} could not be wielded with a free hand");
    }

    /// <summary>
    /// Every hand-held _SW gun with a magazine slot reloads while held.
    /// </summary>
    [Test]
    public async Task ReloadEveryHeldSWGun()
    {
        var factory = SEntMan.ComponentFactory;
        var item = factory.GetComponentName<ItemComponent>();
        var magazineProvider = factory.GetComponentName<MagazineAmmoProviderComponent>();
        var itemSlots = factory.GetComponentName<ItemSlotsComponent>();
        var guns = ProtoMan.EnumeratePrototypes<EntityPrototype>()
            .Where(p => !p.Abstract
                        && p.ID.StartsWith("SW")
                        && p.Components.ContainsKey(item)
                        && p.Components.ContainsKey(magazineProvider)
                        && p.Components.ContainsKey(itemSlots))
            .Select(p => p.ID)
            .OrderBy(id => id)
            .ToList();

        Assert.That(guns, Is.Not.Empty);

        var failures = new List<string>();
        var tested = 0;
        foreach (var gun in guns)
        {
            if (await TryReloadInHand(gun, skipWithoutMagazine: true) is { } failure)
            {
                if (failure.Length == 0)
                    continue;

                failures.Add(failure);
            }

            tested++;
        }

        Assert.That(tested, Is.GreaterThan(0));

        Assert.That(failures, Is.Empty, string.Join("\n", failures));
    }

    /// <returns>Null when the gun reloaded in hand, an empty string when it was skipped
    /// (no starting magazine), otherwise what went wrong.</returns>
    private async Task<string?> TryReloadInHand(string gunId, bool skipWithoutMagazine = false)
    {
        var gunSystem = SEntMan.System<SharedGunSystem>();
        var slots = SEntMan.System<ItemSlotsSystem>();

        Assert.That(Hands, Is.Not.Null);
        var handIds = Hands!.SortedHands.ToList();
        Assert.That(handIds, Has.Count.GreaterThanOrEqualTo(2));
        var gunHand = handIds[0];
        var magazineHand = handIds[1];

        // Clear both hands.
        await Server.WaitPost(() =>
        {
            foreach (var hand in handIds)
            {
                // Drop before deleting, so hand deselection runs as in a real game.
                if (HandSys.GetHeldItem((SPlayer, Hands), hand) is { } held)
                {
                    HandSys.TryDrop((SPlayer, Hands), hand, checkActionBlocker: false);
                    SEntMan.DeleteEntity(held);
                }
            }

            HandSys.TrySetActiveHand((SPlayer, Hands), gunHand);
        });
        await RunTicks(5);

        var gunNet = await PlaceInHands(gunId);
        var gun = ToServer(gunNet);

        // A gun that takes both hands for good (MultiHandedItem) leaves no hand for the magazine.
        if (HasComp<MultiHandedItemComponent>(gunNet))
            return $"{gunId}: takes both hands while held, so no hand is free for a magazine";

        // Take the starting magazine out and put it in the other hand.
        var ejected = false;
        var hasMagazine = false;
        await Server.WaitPost(() =>
        {
            hasMagazine = slots.TryGetSlot(gun, MagazineSlot, out var slot) && slot.HasItem
                          && gunSystem.GetAmmoCount(gun) > 0;
            if (!hasMagazine)
                return;

            if (!slots.TryEject(gun, MagazineSlot, null, out var magazine) || magazine == null)
                return;

            ejected = HandSys.TryPickup(SPlayer, magazine.Value, magazineHand, checkActionBlocker: false, animate: false, handsComp: Hands)
                      && HandSys.TrySetActiveHand((SPlayer, Hands), magazineHand);
        });
        await RunTicks(5);

        if (!hasMagazine && skipWithoutMagazine)
            return string.Empty;

        if (!ejected)
            return $"{gunId}: could not take the magazine out and hold it in the other hand";

        if (HandSys.GetHeldItem((SPlayer, Hands), gunHand) != gun)
            return $"{gunId}: the gun left the hand when the magazine came out";

        var emptyAmmo = 0;
        await Server.WaitPost(() => emptyAmmo = gunSystem.GetAmmoCount(gun));

        // The player clicks the hand that holds the gun, with the magazine in the active hand.
        await Client.WaitPost(() => CEntMan.RaisePredictiveEvent(new RequestHandInteractUsingEvent(gunHand)));
        await RunTicks(10);

        var ammo = 0;
        await Server.WaitPost(() => ammo = gunSystem.GetAmmoCount(gun));

        if (ammo <= emptyAmmo)
            return $"{gunId}: ammo stayed at {ammo} after reloading in hand (was {emptyAmmo})";

        if (HandSys.GetHeldItem((SPlayer, Hands), gunHand) != gun)
            return $"{gunId}: the gun left the hand while reloading";

        return null;
    }
}
