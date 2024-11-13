using System.Linq;
using System.Numerics;
using Content.Server._SW.Force.Components;
using Content.Server.Actions;
using Content.Server.Hands.Systems;
using Content.Server.Popups;
using Content.Shared._SW;
using Content.Shared.Actions;
using Content.Shared.Item;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Throwing;
using Robust.Server.Containers;
using Robust.Server.GameObjects;
using Robust.Shared.Physics.Components;

namespace Content.Server._SW.Force.Systems;

public sealed class SaberSystem : EntitySystem
{
    [Dependency] private readonly PopupSystem _popupSystem = default!;
    [Dependency] private readonly HandsSystem _handsSystem = default!;
    [Dependency] private readonly PhysicsSystem _physicsSystem = default!;
    [Dependency] private readonly ContainerSystem _containerSystem = default!;
    [Dependency] private readonly ThrowingSystem _throwingSystem = default!;
    [Dependency] private readonly TransformSystem _transformSystem = default!;
    [Dependency] private readonly ActionsSystem _actionsSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SaberComponent, GettingPickedUpAttemptEvent>(OnSaberEquipped);
        SubscribeLocalEvent<CallSaberAbilityEvent>(OnCallSaber);
        SubscribeLocalEvent<SaberComponent, ComponentRemove>(OnComponentRemove);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ForcePulledComponent>();
        while (query.MoveNext(out var saber, out var comp))
        {
            var forceUser = comp.ForceUser;
            if(!TryComp<TransformComponent>(forceUser, out var transformComponent))
                continue;

            if (comp.ForceUser == null)
                continue;

            comp.ActiveCooldown -= frameTime;

            if (_transformSystem.GetGridTilePositionOrDefault(saber) != _transformSystem.GetGridTilePositionOrDefault(((EntityUid) forceUser, transformComponent)) && comp.ActiveCooldown <= 0F)
            {
                comp.ActiveCooldown = comp.Cooldown;
                _throwingSystem.TryThrow(saber, transformComponent.Coordinates, comp.ThrowForce);
            }
            else if (_transformSystem.GetGridTilePositionOrDefault(saber) == _transformSystem.GetGridTilePositionOrDefault(((EntityUid) forceUser, transformComponent)))
            {
                _handsSystem.TryPickupAnyHand((EntityUid) forceUser, saber);
                RemCompDeferred<ForcePulledComponent>(saber);
            }
        }
    }

    private void OnSaberEquipped(EntityUid uid, SaberComponent component, GettingPickedUpAttemptEvent args)
    {
        var user = args.User;

        if (component.Owner != user && component.Owner != null)
        {
            _popupSystem.PopupEntity("Световой меч отвергает вас!", user, PopupType.Medium);
            args.Cancel();
        }

        if (component.Owner == null)
        {
            // TODO: Акшен приватизации чужого меча
           // _actionsSystem.AddAction(user, ref component.CallSaberActionEntity, component.CallSaberAction);
        }
    }

    private void OnCallSaber(CallSaberAbilityEvent args)
    {
        var user = args.Performer;

        var query = EntityQueryEnumerator<SaberComponent>();

        while (query.MoveNext(out var uid, out var saberComponent))
        {
            if (saberComponent.Owner == null || saberComponent.Owner != user)
                continue;

            if (_containerSystem.IsEntityInContainer(uid) && !_containerSystem.TryRemoveFromContainer(uid))
                continue;

            PullSaber(uid, user, saberComponent.Duration);

            _popupSystem.PopupEntity("Под воздействием силы, меч возвращается вам в руки...", uid, PopupType.Medium);
        }

        args.Handled = true;
    }

    private void PullSaber(EntityUid saber, EntityUid forceUser, TimeSpan duration)
    {
        if (!TryComp<PhysicsComponent>(saber, out var physics))
            return;

        _physicsSystem.SetLinearVelocity(saber, Vector2.Zero, false, body: physics);
        _physicsSystem.SetAngularVelocity(saber, 0, false, body: physics);
        var forcePulledComponent = AddComp<ForcePulledComponent>(saber);

        forcePulledComponent.ForceUser = forceUser;
    }

    private void OnComponentRemove(EntityUid saber, SaberComponent component, ComponentRemove args)
    {
        if (component.Owner == null || component.CallSaberActionEntity == null)
            return;

        _actionsSystem.RemoveAction(component.CallSaberActionEntity);
    }
}
