using Content.Server.Chat.Systems;
using Content.Server.Movement.Systems;
using Content.Shared.Chat;
using Content.Shared.Effects;
using Content.Shared.Speech.Components;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Map;
using Robust.Shared.Player;
using System.Linq;
using System.Numerics;

namespace Content.Server.Weapons.Melee;

public sealed class MeleeWeaponSystem : SharedMeleeWeaponSystem
{
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly LagCompensationSystem _lag = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedColorFlashEffectSystem _color = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MeleeSpeechComponent, MeleeHitEvent>(OnSpeechHit);
    }

    protected override bool ArcRaySuccessful(EntityUid targetUid,
        Vector2 position,
        Angle angle,
        Angle arcWidth,
        float range,
        MapId mapId,
        EntityUid ignore,
        bool enforceArc,
        ICommonSession? session)
    {
        // Originally the client didn't predict damage effects so you'd intuit some level of how far
        // in the future you'd need to predict, but then there was a lot of complaining like "why would you add artifical delay" as if ping is a choice.
        // Now damage effects are predicted but for wide attacks it differs significantly from client and server so your game could be lying to you on hits.
        // This isn't fair in the slightest because it makes ping a huge advantage and this would be a hidden system.
        // Now the client tells us what they hit and we validate if it's plausible.

        // Even if the client is sending entities they shouldn't be able to hit:
        // A) Wide-damage is split anyway
        // B) We run the same validation we do for click attacks.

        if (!enforceArc)
            return Interaction.InRangeUnobstructed(ignore, targetUid, range + 0.1f, overlapCheck: false);

        if (!TryComp<TransformComponent>(targetUid, out var targetTransform))
            return false;

        var (coordinates, rotation) = _lag.GetCoordinatesAngle(targetUid, session, targetTransform);
        var target = TransformSystem.ToMapCoordinates(coordinates);
        if (target.MapId != mapId)
            return false;

        // A ray may hit the edge of a target that straddles the attack cone.
        var worldRotation = TransformSystem.GetWorldRotation(coordinates.EntityId) + rotation;
        var bounds = _lookup.GetAABBNoContainer(targetUid, target.Position, worldRotation);
        var offset = bounds.Center - position;
        var distance = offset.Length();
        var radius = bounds.Size.Length() / 2;
        if (distance > radius)
        {
            var aimError = Math.Abs(Angle.ShortestDistance(angle, offset.ToWorldAngle()).Theta);
            if (aimError > arcWidth.Theta / 2 + Math.Asin(radius / distance))
                return false;
        }

        return Interaction.InRangeUnobstructed(ignore, targetUid, coordinates, rotation, range + 0.1f, overlapCheck: false);
    }


    protected override bool InRange(EntityUid user, EntityUid target, float range, ICommonSession? session)
    {
        EntityCoordinates targetCoordinates;
        Angle targetLocalAngle;

        if (session is { } pSession)
        {
            (targetCoordinates, targetLocalAngle) = _lag.GetCoordinatesAngle(target, pSession);
            return Interaction.InRangeUnobstructed(user, target, targetCoordinates, targetLocalAngle, range, overlapCheck: false);
        }

        return Interaction.InRangeUnobstructed(user, target, range);
    }

    protected override void DoDamageEffect(List<EntityUid> targets, EntityUid? user, TransformComponent targetXform)
    {
        var filter = Filter.Pvs(targetXform.Coordinates, entityMan: EntityManager).RemoveWhereAttachedEntity(o => o == user);
        _color.RaiseEffect(Color.Red, targets, filter);
    }

    public override void DoLunge(EntityUid user, EntityUid weapon, Angle angle, Vector2 localPos, string? animation, bool predicted = true)
    {
        Filter filter;

        if (predicted)
        {
            filter = Filter.PvsExcept(user, entityManager: EntityManager);
        }
        else
        {
            filter = Filter.Pvs(user, entityManager: EntityManager);
        }

        RaiseNetworkEvent(new MeleeLungeEvent(GetNetEntity(user), GetNetEntity(weapon), angle, localPos, animation), filter);
    }

    private void OnSpeechHit(EntityUid owner, MeleeSpeechComponent comp, MeleeHitEvent args)
    {
        if (!args.IsHit ||
        !args.HitEntities.Any())
        {
            return;
        }

        if (comp.Battlecry != null)//If the battlecry is set to empty, doesn't speak
        {
            _chat.TrySendInGameICMessage(args.User, comp.Battlecry, InGameICChatType.Speak, true, true, checkRadioPrefix: false);  //Speech that isn't sent to chat or adminlogs
        }

    }
}
