using System.Numerics;
using Content.Server._SW.Force.Components;
using Content.Server.Actions;
using Content.Server.Hands.Systems;
using Content.Server.Popups;
using Content.Shared._SW;
using Content.Shared.Actions;
using Content.Shared.Damage;
using Content.Shared.Item;
using Content.Shared.Popups;
using Content.Shared.Throwing;
using Robust.Server.Containers;
using Robust.Server.GameObjects;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;

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
    [Dependency] private readonly DamageableSystem _damageableSystem = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SaberComponent, GettingPickedUpAttemptEvent>(OnSaberEquipped);
        SubscribeLocalEvent<CallSaberAbilityEvent>(OnCallSaber);
        SubscribeLocalEvent<SaberComponent, ComponentRemove>(OnComponentRemove);
        SubscribeLocalEvent<SaberComponent, SaberPrivateAbilityEvent>(OnSaberPrivate);
        SubscribeLocalEvent<SaberComponent, GetItemActionsEvent>(OnGetItemActions);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ForcePulledComponent>();
        while (query.MoveNext(out var saber, out var comp))
        {
            var forceUser = comp.ForceUser;

            if (forceUser == null)
                continue;

            comp.ActiveCooldown -= frameTime;

            if (comp.ForceUser != null)
            {
                if (!TryComp<TransformComponent>(forceUser, out var userTransformComponent))
                    continue;

                if (_transformSystem.GetGridTilePositionOrDefault(saber) !=
                    _transformSystem.GetGridTilePositionOrDefault(((EntityUid) forceUser, userTransformComponent)) &&
                    comp.ActiveCooldown <= 0F)
                {
                    if (comp.Attempts >= comp.MaxAttempts)
                    {
                        RemCompDeferred<ForcePulledComponent>(saber);
                        continue;
                    }

                    comp.ActiveCooldown = comp.Cooldown;
                    comp.Attempts += 1;
                    _throwingSystem.TryThrow(saber, userTransformComponent.Coordinates, comp.ThrowForce);
                }
                else if (_transformSystem.GetGridTilePositionOrDefault(saber) ==
                         _transformSystem.GetGridTilePositionOrDefault(((EntityUid) forceUser, userTransformComponent)))
                {
                    _handsSystem.TryPickupAnyHand((EntityUid) forceUser, saber);
                    RemCompDeferred<ForcePulledComponent>(saber);
                }
            }
        }
    }

    private void OnGetItemActions(EntityUid saber, SaberComponent component, GetItemActionsEvent args)
    {
        if(component.Owner == args.User && component.Owner != null)
            return;

        args.AddAction(ref component.PrivateSaberActionEntity, component.PrivateSaberAction);
    }

    private void OnSaberEquipped(EntityUid uid, SaberComponent component, GettingPickedUpAttemptEvent args)
    {
        var user = args.User;

        if (component.Owner == user || component.Owner == null)
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

            if (_handsSystem.IsHolding(user, uid))
            {
                _popupSystem.PopupEntity("Вы не можете призвать меч, который и так у вас в руке!", user, user, PopupType.MediumCaution);
                continue;
            }

            if (_containerSystem.IsEntityInContainer(uid) && !_containerSystem.TryRemoveFromContainer(uid))
                continue;

            PullSaber(uid, user);

            _popupSystem.PopupEntity("Под воздействием силы, меч возвращается вам в руки...", user, user, PopupType.Medium);
        }

        args.Handled = true;
    }

    private void PullSaber(EntityUid saber, EntityUid forceUser)
    {
        if (!TryComp<PhysicsComponent>(saber, out var physics))
            return;

        _physicsSystem.SetLinearVelocity(saber, Vector2.Zero, false, body: physics);
        _physicsSystem.SetAngularVelocity(saber, 0, false, body: physics);
        var forcePulledComponent = AddComp<ForcePulledComponent>(saber);

        forcePulledComponent.ForceUser = forceUser;
    }

    private void OnSaberPrivate(EntityUid saber, SaberComponent component, SaberPrivateAbilityEvent args)
    {
        if (component.Owner == args.Performer || component.Owner != null)
            return;

        if(!TryComp<ForceComponent>(args.Performer, out var forceComponent))
            return;

        component.Owner = args.Performer;
        _popupSystem.PopupEntity("Вы успешно подчинили меч!", args.Performer, PopupType.Medium);
        _actionsSystem.RemoveAction(component.PrivateSaberActionEntity);
        _actionsSystem.AddAction(args.Performer, component.CallSaberAction);
        forceComponent.OwnedSabers.Add(saber);

        args.Handled = true;
    }

    private void OnComponentRemove(EntityUid saber, SaberComponent component, ComponentRemove args)
    {
        if (component.Owner == null || component.CallSaberActionEntity == null)
            return;

        _actionsSystem.RemoveAction(component.CallSaberActionEntity);
    }
}
