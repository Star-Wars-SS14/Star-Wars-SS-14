using Content.Shared.Hands;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Item;
using Content.Shared.Popups;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Robust.Shared.GameStates;
using Robust.Shared.Network;

namespace Content.Shared._SW.GunPreparation;

/// <summary>Restores the two-free-hands pickup requirement for heavy weapons.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class SWAutoWieldOnPickupComponent : Component;

public sealed class SWAutoWieldOnPickupSystem : EntitySystem
{
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedWieldableSystem _wieldable = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWAutoWieldOnPickupComponent, GettingPickedUpAttemptEvent>(OnAttemptPickup);
        SubscribeLocalEvent<SWAutoWieldOnPickupComponent, GotEquippedHandEvent>(OnEquipped);
    }

    private void OnAttemptPickup(Entity<SWAutoWieldOnPickupComponent> ent, ref GettingPickedUpAttemptEvent args)
    {
        // A move between this user's hands is checked before the original hand is freed.
        var handsNeeded = _hands.IsHolding(args.User, ent.Owner) ? 1 : 2;
        if (args.Cancelled || _hands.CountFreeHands(args.User) >= handsNeeded)
            return;

        args.Cancel();
        if (args.ShowPopup)
            _popup.PopupPredictedCursor(Loc.GetString("multi-handed-item-pick-up-fail",
                ("number", 1), ("item", ent.Owner)), args.User);
    }

    private void OnEquipped(Entity<SWAutoWieldOnPickupComponent> ent, ref GotEquippedHandEvent args)
    {
        if (!TryComp<WieldableComponent>(ent, out var wieldable) || wieldable.Wielded)
            return;

        // Never drop another item if an attempt handler fills a hand during wielding.
        if (_hands.CountFreeHands(args.User) >= wieldable.FreeHandsRequired &&
            _wieldable.TryWield(ent.Owner, wieldable, args.User, dropOthers: false, quiet: true))
            return;

        if (wieldable.Wielded)
            _wieldable.TryUnwield(ent.Owner, wieldable, args.User, force: true);

        if (_net.IsServer)
            _popup.PopupEntity(Loc.GetString("sw-auto-wield-pickup-failed"), args.User, args.User);
    }
}
