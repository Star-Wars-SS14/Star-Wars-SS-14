using System.Numerics;
using Content.Server.Movement.Systems;
using Content.Shared._SW._Senate.Chamber;
using Content.Shared.Buckle.Components;
using Content.Shared.Movement.Components;

namespace Content.Server._SW._Senate.Debate;

public sealed class SWSenateChamberSeatSystem : EntitySystem
{
    [Dependency] private readonly ContentEyeSystem _eye = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<SWSenateChamberSeatComponent, StrappedEvent>(OnStrapped);
        SubscribeLocalEvent<SWSenateChamberSeatComponent, UnstrappedEvent>(OnUnstrapped);
    }

    private void OnStrapped(Entity<SWSenateChamberSeatComponent> ent, ref StrappedEvent args)
    {
        var player = args.Buckle.Owner;
        if (!TryComp<ContentEyeComponent>(player, out var contentEye))
            return;

        var marker = FindMarker(ent.Owner);
        if (marker == null)
            return;

        var view = EnsureComp<SWSenateChamberViewComponent>(player);
        if (view.Applied)
            return;

        view.PreviousZoom = contentEye.TargetZoom;
        view.PreviousMaxZoom = contentEye.MaxZoom;
        view.Offset = _transform.GetWorldPosition(marker.Value) - _transform.GetWorldPosition(player);
        view.PvsScale = marker.Value.Comp.PvsScale;
        view.Applied = true;
        Dirty(player, view);

        _eye.SetMaxZoom(player, Vector2.Max(contentEye.MaxZoom, marker.Value.Comp.Zoom), contentEye);
        _eye.SetZoom(player, marker.Value.Comp.Zoom, eye: contentEye);
        _eye.UpdatePvsScale(player, contentEye);
    }

    private void OnUnstrapped(Entity<SWSenateChamberSeatComponent> ent, ref UnstrappedEvent args)
    {
        Restore(args.Buckle.Owner);
    }

    private void Restore(EntityUid player)
    {
        if (!TryComp<SWSenateChamberViewComponent>(player, out var view) || !view.Applied)
            return;

        view.Applied = false;

        if (TryComp<ContentEyeComponent>(player, out var contentEye))
        {
            if (view.PreviousMaxZoom is { } maxZoom)
                _eye.SetMaxZoom(player, maxZoom, contentEye);

            if (view.PreviousZoom is { } zoom)
                _eye.SetZoom(player, zoom, eye: contentEye);

            _eye.UpdatePvsScale(player, contentEye);
        }

        view.PreviousZoom = null;
        view.PreviousMaxZoom = null;
        Dirty(player, view);
    }

    private Entity<SWSenateChamberMarkerComponent>? FindMarker(EntityUid near)
    {
        var nearMap = Transform(near).MapID;
        var nearPos = _transform.GetWorldPosition(near);

        Entity<SWSenateChamberMarkerComponent>? best = null;
        var bestDist = float.MaxValue;

        var query = EntityQueryEnumerator<SWSenateChamberMarkerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out var xform))
        {
            if (xform.MapID != nearMap)
                continue;

            var dist = (_transform.GetWorldPosition(uid) - nearPos).LengthSquared();
            if (dist >= bestDist)
                continue;

            bestDist = dist;
            best = (uid, comp);
        }

        return best;
    }
}
