using System.Numerics;
using Content.Shared.Doors.Components;
using Content.Shared.Physics;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

namespace Content.Client._SW.Tutorial;

/// <summary>
/// Shows where to go instead of saying it: a thin beam with chevrons along a walkable route from the student to the
/// target, and pulsing brackets on the target. Drawn after the field of view, so a target behind a wall still has a direction.
/// A step done flashes a green tick on the target it finished.
/// </summary>
public sealed class SWTutorialFocusOverlay : Overlay
{
    /// <summary>
    /// One pixel of a sprite in the game's 32 px tile. The shapes are built on this grid to match the sprites.
    /// </summary>
    private const float Pixel = 1f / 32f;

    private const float ChevronSpacing = 0.9f;
    private const float TrailStart = 1f;
    private const float TrailEndGap = 0.7f;
    private const float TrailMaxLength = 16f;
    private const float RewardTime = 0.6f;
    private const int RouteSearchLimit = 6000;
    private const float RouteRefresh = 0.4f;
    private const float PulseSpeed = 3.2f;
    private const float BeamHalf = Pixel;
    private const float BeamAlpha = 0.4f;

    /// <summary>
    /// At most 2 Hz, the limit for anything that flashes.
    /// </summary>
    private const float UrgentPulseSpeed = 9f;

    private static readonly Color Mark = Color.White;
    private static readonly Color Shade = Color.FromHex("#101010");
    private static readonly Color Green = Color.FromHex("#5CE65C");

    private static readonly Vector2i[] Steps =
    {
        new(1, 0), new(-1, 0), new(0, 1), new(0, -1), new(1, 1), new(1, -1), new(-1, 1), new(-1, -1),
    };

    [Dependency] private readonly IEntityManager _entities = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPlayerManager _player = default!;

    private readonly TransformSystem _transform;
    private readonly SharedMapSystem _map;
    private readonly List<Vector2> _route = new();

    private NetEntity? _focusEntity;
    private NetCoordinates? _focusCoordinates;
    private TimeSpan? _rewardAt;
    private Vector2? _rewardPosition;
    private Vector2? _lastTarget;
    private Vector2i? _routeFrom;
    private Vector2i? _routeTo;
    private TimeSpan _routeBuilt;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    /// <summary>
    /// 0 normally, 1 when the student has been stuck: bigger and faster, with no new words.
    /// </summary>
    public float Urgency { get; set; }

    /// <summary>
    /// No pulse and no flow, only size.
    /// </summary>
    public bool ReducedMotion { get; set; }

    public SWTutorialFocusOverlay()
    {
        IoCManager.InjectDependencies(this);
        _transform = _entities.System<TransformSystem>();
        _map = _entities.System<SharedMapSystem>();
        ZIndex = 200;
    }

    public void SetFocus(NetEntity? entity, NetCoordinates? coordinates)
    {
        _focusEntity = entity;
        _focusCoordinates = coordinates;
        _routeTo = null;
    }

    public void Reward()
    {
        _rewardAt = _timing.RealTime;
        _rewardPosition = _lastTarget;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (_player.LocalEntity is not { } body || !_entities.TryGetComponent<TransformComponent>(body, out var bodyXform))
            return;

        var handle = args.WorldHandle;
        var time = (float) _timing.RealTime.TotalSeconds;
        var from = _transform.GetWorldPosition(bodyXform);

        if (GetTarget() is { } target && target.MapId == bodyXform.MapID)
        {
            _lastTarget = target.Position;
            UpdateRoute(bodyXform, from, target.Position);
            DrawTrail(handle, time);
            DrawBrackets(handle, target.Position, time);
        }
        else
        {
            _lastTarget = null;
        }

        if (_rewardAt is { } rewardAt)
            DrawReward(handle, _rewardPosition ?? from, rewardAt);

        handle.SetTransform(Matrix3x2.Identity);
    }

    private MapCoordinates? GetTarget()
    {
        if (_focusEntity is { } net
            && _entities.TryGetEntity(net, out var entity)
            && _entities.TryGetComponent<TransformComponent>(entity, out var xform))
        {
            return _transform.GetMapCoordinates(entity.Value, xform);
        }

        if (_focusCoordinates is { } coordinates)
        {
            var local = _entities.GetCoordinates(coordinates);
            if (local.IsValid(_entities))
                return _transform.ToMapCoordinates(local);
        }

        return null;
    }

    /// <summary>
    /// A breadth-first walk over free tiles, because a straight line through a wall lies about where to go.
    /// </summary>
    private void UpdateRoute(TransformComponent bodyXform, Vector2 from, Vector2 to)
    {
        if (bodyXform.GridUid is not { } gridUid || !_entities.TryGetComponent<MapGridComponent>(gridUid, out var grid))
        {
            SetStraightRoute(from, to);
            return;
        }

        var start = _map.WorldToTile(gridUid, grid, from);
        var goal = _map.WorldToTile(gridUid, grid, to);
        var fresh = (_timing.RealTime - _routeBuilt).TotalSeconds < RouteRefresh;
        if (start == _routeFrom && goal == _routeTo && fresh)
        {
            // The ends follow smoothly between rebuilds of the tiles in the middle.
            if (_route.Count >= 2)
            {
                _route[0] = from;
                _route[^1] = to;
            }
            return;
        }

        _routeFrom = start;
        _routeTo = goal;
        _routeBuilt = _timing.RealTime;

        if (!BuildRoute(gridUid, grid, start, goal, from, to))
            SetStraightRoute(from, to);
    }

    private void SetStraightRoute(Vector2 from, Vector2 to)
    {
        _route.Clear();
        _route.Add(from);
        _route.Add(to);
    }

    private bool BuildRoute(EntityUid gridUid, MapGridComponent grid, Vector2i start, Vector2i goal, Vector2 from, Vector2 to)
    {
        var cameFrom = new Dictionary<Vector2i, Vector2i> { [start] = start };
        var queue = new Queue<Vector2i>();
        queue.Enqueue(start);

        while (queue.TryDequeue(out var tile) && cameFrom.Count < RouteSearchLimit)
        {
            if (tile == goal)
                break;

            foreach (var step in Steps)
            {
                var next = tile + step;
                if (cameFrom.ContainsKey(next))
                    continue;

                // The target may stand on a blocked tile, such as a machine.
                if (next != goal && !IsWalkable(gridUid, grid, next))
                    continue;

                // No cutting a corner between two walls.
                if (step.X != 0 && step.Y != 0
                    && (!IsWalkable(gridUid, grid, tile + new Vector2i(step.X, 0))
                        || !IsWalkable(gridUid, grid, tile + new Vector2i(0, step.Y))))
                    continue;

                cameFrom[next] = tile;
                queue.Enqueue(next);
            }
        }

        if (!cameFrom.ContainsKey(goal))
            return false;

        _route.Clear();
        _route.Add(to);
        for (var tile = cameFrom[goal]; tile != start; tile = cameFrom[tile])
        {
            var center = _map.GridTileToLocal(gridUid, grid, tile);
            _route.Add(_transform.ToMapCoordinates(center).Position);
        }
        _route.Add(from);
        _route.Reverse();
        return true;
    }

    private bool IsWalkable(EntityUid gridUid, MapGridComponent grid, Vector2i tile)
    {
        if (!_map.TryGetTileRef(gridUid, grid, tile, out var tileRef) || tileRef.Tile.IsEmpty)
            return false;

        var anchored = _map.GetAnchoredEntitiesEnumerator(gridUid, grid, tile);
        while (anchored.MoveNext(out var uid))
        {
            if (!_entities.TryGetComponent<PhysicsComponent>(uid, out var physics) || !physics.Hard || !physics.CanCollide)
                continue;

            if ((physics.CollisionLayer & (int) CollisionGroup.Impassable) == 0)
                continue;

            // A door opens as the student walks up to it.
            if (!_entities.HasComponent<DoorComponent>(uid))
                return false;
        }

        return true;
    }

    private void DrawTrail(DrawingHandleWorld handle, float time)
    {
        var scale = 1f + Urgency * 0.5f;
        var speed = 2.2f + Urgency * 1.8f;
        var flow = ReducedMotion ? 0f : time * speed % ChevronSpacing;

        var length = 0f;
        for (var i = 0; i + 1 < _route.Count; i++)
        {
            length += (_route[i + 1] - _route[i]).Length();
        }

        var end = Math.Min(length - TrailEndGap, TrailMaxLength);
        DrawBeam(handle, end);

        for (var distance = TrailStart + flow; distance < end; distance += ChevronSpacing)
        {
            if (!TryGetRoutePoint(distance, out var position, out var direction))
                break;

            // Fades in at both ends, so a chevron never pops into view.
            var edge = Math.Min((distance - TrailStart) / 0.5f, (end - distance) / 0.5f);
            var alpha = ReducedMotion ? 1f : Math.Clamp(edge, 0f, 1f);
            handle.SetTransform(Matrix3Helpers.CreateTransform(position, direction.ToAngle()));
            DrawChevron(handle, scale, Shade.WithAlpha(alpha * 0.85f), 1f);
            DrawChevron(handle, scale, Mark.WithAlpha(alpha), 0f);
        }
    }

    /// <summary>
    /// A thin faint line, so the eye can follow the way past a corner and the floor stays readable through it.
    /// </summary>
    private void DrawBeam(DrawingHandleWorld handle, float end)
    {
        var walked = 0f;
        for (var i = 0; i + 1 < _route.Count && walked < end; i++)
        {
            var segment = _route[i + 1] - _route[i];
            var length = segment.Length();
            if (length <= 0f)
                continue;

            var from = Math.Max(TrailStart - walked, 0f);
            var to = Math.Min(end - walked, length);
            walked += length;
            if (to <= from)
                continue;

            handle.SetTransform(Matrix3Helpers.CreateTransform(_route[i], (segment / length).ToAngle()));
            handle.DrawRect(new Box2(from, -BeamHalf - Pixel, to, BeamHalf + Pixel), Shade.WithAlpha(BeamAlpha * 0.6f));
            handle.DrawRect(new Box2(from, -BeamHalf, to, BeamHalf), Mark.WithAlpha(BeamAlpha));
        }
    }

    private bool TryGetRoutePoint(float distance, out Vector2 position, out Vector2 direction)
    {
        for (var i = 0; i + 1 < _route.Count; i++)
        {
            var segment = _route[i + 1] - _route[i];
            var length = segment.Length();
            if (length <= 0f)
                continue;

            if (distance <= length)
            {
                direction = segment / length;
                position = _route[i] + direction * distance;
                return true;
            }

            distance -= length;
        }

        position = default;
        direction = default;
        return false;
    }

    /// <summary>
    /// A stair of squares pointing along +X. The dark copy is one pixel larger each way and keeps it readable on any floor.
    /// </summary>
    private static void DrawChevron(DrawingHandleWorld handle, float scale, Color color, float outline)
    {
        const int reach = 5;
        var pixel = Pixel * 2f * scale;
        var pad = outline * Pixel;
        for (var i = -reach; i < reach; i++)
        {
            // Measured from the middle of the row, so both arms are the same length.
            var row = i + 0.5f;
            var x = (reach - Math.Abs(row)) * pixel;
            var y = i * pixel;
            handle.DrawRect(new Box2(x - pixel * 2f - pad, y - pad, x + pixel + pad, y + pixel + pad), color);
        }
    }

    private void DrawBrackets(DrawingHandleWorld handle, Vector2 at, float time)
    {
        handle.SetTransform(Matrix3Helpers.CreateTranslation(at));

        var speed = PulseSpeed + Urgency * (UrgentPulseSpeed - PulseSpeed);
        var pulse = ReducedMotion ? 0.5f : (MathF.Sin(time * speed) + 1f) / 2f;
        var half = 0.62f + 0.1f * pulse + Urgency * 0.25f;

        // A faint ring leaving the target, for the eye to catch at the edge of the view.
        if (!ReducedMotion)
        {
            var ripple = time * (0.8f + Urgency * 0.6f) % 1f;
            handle.DrawCircle(Vector2.Zero, 0.35f + ripple * (0.9f + Urgency * 0.6f), Mark.WithAlpha((1f - ripple) * 0.5f), filled: false);
        }

        var length = 0.26f + Urgency * 0.08f;
        var thick = Pixel * (3f + Urgency * 2f);
        DrawCorners(handle, half + Pixel, length + Pixel, thick + Pixel * 2f, Shade.WithAlpha(0.85f));
        DrawCorners(handle, half, length, thick, Mark);
    }

    private static void DrawCorners(DrawingHandleWorld handle, float half, float length, float thick, Color color)
    {
        foreach (var (x, y) in new[] { (-1f, -1f), (1f, -1f), (-1f, 1f), (1f, 1f) })
        {
            var corner = new Vector2(x * half, y * half);
            handle.DrawRect(Box2.FromTwoPoints(corner, corner - new Vector2(x * length, y * thick)), color);
            handle.DrawRect(Box2.FromTwoPoints(corner, corner - new Vector2(x * thick, y * length)), color);
        }
    }

    private void DrawReward(DrawingHandleWorld handle, Vector2 at, TimeSpan rewardAt)
    {
        var age = (float) (_timing.RealTime - rewardAt).TotalSeconds / RewardTime;
        if (age >= 1f)
        {
            _rewardAt = null;
            return;
        }

        handle.SetTransform(Matrix3Helpers.CreateTranslation(at));
        handle.DrawCircle(Vector2.Zero, 0.4f + age * 0.8f, Green.WithAlpha(1f - age), filled: false);

        var lift = new Vector2(0f, 0.15f + age * 0.35f);
        var alpha = Math.Clamp((1f - age) * 2f, 0f, 1f);
        DrawTick(handle, lift, 1f, Shade.WithAlpha(alpha));
        DrawTick(handle, lift, 0f, Green.WithAlpha(alpha));
    }

    private static void DrawTick(DrawingHandleWorld handle, Vector2 at, float outline, Color color)
    {
        const float pixel = Pixel * 2f;
        var pad = outline * Pixel;
        var cells = new (int X, int Y)[]
            { (-4, 0), (-3, -1), (-2, -2), (-1, -3), (0, -2), (1, -1), (2, 0), (3, 1), (4, 2), (5, 3) };
        foreach (var (x, y) in cells)
        {
            var corner = at + new Vector2(x * pixel, y * pixel);
            handle.DrawRect(new Box2(corner.X - pad, corner.Y - pad, corner.X + pixel * 2f + pad, corner.Y + pixel * 2f + pad), color);
        }
    }
}
