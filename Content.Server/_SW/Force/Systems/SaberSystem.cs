using Content.Server._SW.Force.Components;
using Content.Server.Hands.Systems;
using Content.Server.Popups;
using Content.Shared._SW;
using Content.Shared.Item;
using Content.Shared.Popups;

namespace Content.Server._SW.Force.Systems;

public sealed class SaberSystem : EntitySystem
{
    [Dependency] private readonly PopupSystem _popupSystem = default!;
    [Dependency] private readonly HandsSystem _handsSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SaberComponent, GettingPickedUpAttemptEvent>(OnSaberEquipped);
        SubscribeLocalEvent<CallSaberAbilityEvent>(OnCallSaber);
    }

    private void OnSaberEquipped(EntityUid uid, SaberComponent component, GettingPickedUpAttemptEvent args)
    {
        var user = args.User;

        if (component.Owner == user)
            return;

        _popupSystem.PopupEntity("Световой меч отвергает вас!", user, PopupType.Medium);
        args.Cancel();
    }

    private void OnCallSaber(CallSaberAbilityEvent args)
    {
        var user = args.Performer;

        var query = EntityQueryEnumerator<SaberComponent>();

        while (query.MoveNext(out var uid, out var saberComponent))
        {
            if (saberComponent.Owner == null || saberComponent.Owner != user)
                continue;

            _handsSystem.TryPickupAnyHand(user, uid);
            _popupSystem.PopupEntity("Под воздействием силы, меч возвращается вам в руки...", uid, PopupType.Medium);
        }

        args.Handled = true;
    }
}
