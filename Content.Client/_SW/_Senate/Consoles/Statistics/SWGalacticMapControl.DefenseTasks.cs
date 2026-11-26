using System;
using System.Linq;
using System.Numerics;
using Content.Shared._SW._Senate.Ministries;
using Content.Shared.CCVar;
using Robust.Client.Graphics;
using Robust.Shared;
using Robust.Shared.Maths;

namespace Content.Client._SW._Senate.Consoles.Statistics;

public enum SWDefenseTaskIndicator : byte { Shield, Patrol, Training, Repair, Battle }

public sealed partial class SWGalacticMapControl
{
    private double _defenseTaskSeconds;
    public double DefenseTaskAnimationSeconds => _defenseTaskSeconds;

    private void AdvanceDefenseTasks(float seconds)
    {
        if (_defense is not { ClockRunning: true } || _configuration.GetCVar(CCVars.ReducedMotion)
            || !float.IsFinite(seconds) || seconds <= 0) return;
        _defenseTaskSeconds += Math.Min(seconds, .25f);
    }

    public SWDefenseTaskIndicator? DefenseFleetTaskIndicator(int id)
    {
        var fleet = _defense?.Fleets.FirstOrDefault(f => f.Id == id);
        if (fleet == null || fleet.Phase is SWDefenseOperationPhase.Travel or SWDefenseOperationPhase.Preparation or SWDefenseOperationPhase.Lost) return null;
        return fleet.Order switch
        {
            SWDefenseFleetOrder.Respond when fleet.Phase == SWDefenseOperationPhase.Execution => SWDefenseTaskIndicator.Battle,
            SWDefenseFleetOrder.Guard or SWDefenseFleetOrder.Border when fleet.Phase == SWDefenseOperationPhase.Standing => SWDefenseTaskIndicator.Shield,
            SWDefenseFleetOrder.Patrol when fleet.Phase == SWDefenseOperationPhase.Standing => SWDefenseTaskIndicator.Patrol,
            SWDefenseFleetOrder.Train when fleet.Phase == SWDefenseOperationPhase.Standing => SWDefenseTaskIndicator.Training,
            SWDefenseFleetOrder.Repair when fleet.Phase is SWDefenseOperationPhase.Execution or SWDefenseOperationPhase.Recovery => SWDefenseTaskIndicator.Repair,
            _ => null,
        };
    }

    /// <summary>The native line-segment endpoints used to draw a fleet's active task.</summary>
    public Vector2[] DefenseFleetTaskGeometry(int id)
    {
        if (DefenseFleetTaskIndicator(id) is not { } kind || DefenseFleetPosition(id) is not { } center) return Array.Empty<Vector2>();
        // Activity owns a separate lane below the silhouette and its number badge.
        // Keep every glyph inside that tile, including at the extremes of its motion.
        center += new Vector2(0, 26);
        var seconds = (float) (_defenseTaskSeconds + id * .37);
        Vector2 Orbit(float angle, float radius) => center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
        switch (kind)
        {
            case SWDefenseTaskIndicator.Shield:
            {
                var vertices = new[] { new Vector2(-7, -7), new Vector2(7, -7),
                    new Vector2(6, 2), new Vector2(0, 8), new Vector2(-6, 2) };
                var points = new Vector2[vertices.Length * 2];
                var pulse = 1 + MathF.Sin(seconds * MathF.Tau / 6) * .08f;
                for (var i = 0; i < vertices.Length; i++)
                {
                    points[i * 2] = center + vertices[i] * pulse;
                    points[i * 2 + 1] = center + vertices[(i + 1) % vertices.Length] * pulse;
                }
                return points;
            }
            case SWDefenseTaskIndicator.Patrol:
            {
                var points = new Vector2[12];
                var angle = seconds * MathF.Tau / 9;
                for (var i = 0; i < 6; i++)
                {
                    points[i * 2] = Orbit(angle + i * .5f, 8);
                    points[i * 2 + 1] = Orbit(angle + (i + 1) * .5f, 8);
                }
                return points;
            }
            case SWDefenseTaskIndicator.Training:
            {
                var points = new Vector2[6];
                for (var i = 0; i < 3; i++)
                {
                    var length = 3 + (MathF.Sin(seconds * MathF.Tau / 4 - i * 1.2f) + 1) * 1.5f;
                    points[i * 2] = center + new Vector2(-4, -6 + i * 6);
                    points[i * 2 + 1] = points[i * 2] + new Vector2(length, 0);
                }
                return points;
            }
            case SWDefenseTaskIndicator.Repair:
            {
                var points = new Vector2[8];
                for (var i = 0; i < 4; i++)
                {
                    var angle = seconds * MathF.Tau / 8 + i * MathF.PI / 2;
                    points[i * 2] = Orbit(angle, 8);
                    points[i * 2 + 1] = Orbit(angle + .7f, 8);
                }
                return points;
            }
            default:
            {
                var phase = seconds / 4 % 1;
                var upper = Vector2.Lerp(center + new Vector2(-8, -7), center + new Vector2(1, -2), phase);
                var lower = Vector2.Lerp(center + new Vector2(8, 7), center + new Vector2(-1, 2), phase);
                var spark = center;
                var radius = 2 + MathF.Sin(seconds * MathF.Tau / 5) * .7f;
                return new[] { upper, upper + new Vector2(4, 2), lower, lower + new Vector2(-4, 2),
                    spark - new Vector2(radius, radius), spark + new Vector2(radius, radius),
                    spark + new Vector2(-radius, radius), spark + new Vector2(radius, -radius) };
            }
        }
    }

    private void DrawDefenseTask(DrawingHandleScreen handle, int id)
    {
        if (DefenseFleetTaskIndicator(id) is not { } kind) return;
        var color = kind switch
        {
            SWDefenseTaskIndicator.Battle => SWConsoleStyle.Bad,
            SWDefenseTaskIndicator.Repair => SWConsoleStyle.Brass,
            SWDefenseTaskIndicator.Shield => SWConsoleStyle.Good,
            _ => SWConsoleStyle.Accent,
        };
        if (DefenseFleetPosition(id) is not { } position) return;
        var center = position + new Vector2(0, 26);
        var box = new UIBox2((center - new Vector2(12, 11)) * UIScale,
            (center + new Vector2(12, 11)) * UIScale);
        handle.DrawRect(box, SWConsoleStyle.Navigation);
        handle.DrawRect(box, color.WithAlpha(.65f), false);
        var points = DefenseFleetTaskGeometry(id);
        // A dark under-stroke preserves contrast over routes in both map palettes.
        for (var i = 0; i + 1 < points.Length; i += 2)
        {
            Stroke(handle, points[i] * UIScale, points[i + 1] * UIScale, SWConsoleStyle.Navigation, 3.5f);
            Stroke(handle, points[i] * UIScale, points[i + 1] * UIScale, color, 1.8f);
        }
    }
}
