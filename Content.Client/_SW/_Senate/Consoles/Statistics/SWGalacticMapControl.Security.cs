using System;
using System.Linq;
using System.Numerics;
using Content.Shared._SW._Senate.Ministries;
using Robust.Client.Graphics;
using Robust.Shared.Maths;

namespace Content.Client._SW._Senate.Consoles.Statistics;

public sealed partial class SWGalacticMapControl
{
    private SWSecurityMapState? _security;
    private int? _securitySelected;
    private double _securityElapsed;
    private double _securityAnimationSeconds;
    public event Action<int>? SecurityTeamSelected;
    public event Action<int>? SecurityCaseSelected;
    public event Action<string>? SecurityCellSelected;
    private Vector2? SecurityWorldPosition(string id)
    {
        if (_worlds.TryGetValue(id, out var world)) return MapToLocal(world.Position);
        var sector = _security?.Worlds.FirstOrDefault(w => w.Id == id)?.SectorId;
        var anchor = _map.Worlds.FirstOrDefault(w => w.Anchor && w.Sector?.Id == sector);
        return anchor == null ? null : MapToLocal(anchor.Position);
    }
    public bool FocusSecurityTeam(int id)
    {
        var team = _security?.Teams.FirstOrDefault(t => t.Id == id);
        if (team == null || SecurityPosition(team) is not { } position) return false;
        _pan += Size / 2 - position; ClampPan(); ViewUpdated(); return true;
    }

    public void SetSecurity(SWSecurityMapState? state, int? selected)
    {
        _security = state;
        _securitySelected = selected;
        _securityElapsed = 0;
    }
    private void AdvanceSecurity(float seconds)
    {
        if (!float.IsFinite(seconds) || seconds <= 0) return;
        _securityAnimationSeconds += Math.Min(seconds, .25f);
        if (_security is { ClockRunning: true, RealSecondsPerHour: > 0 })
            _securityElapsed += Math.Min(seconds, .25f) / _security.RealSecondsPerHour;
    }
    private Vector2? SecurityPosition(SWSecurityTeamView team)
    {
        if (team.Phase == SWSecurityTeamPhase.Lost) return null;
        var origin = SecurityWorldPosition(team.WorldId);
        if (origin == null) return null;
        if (team.Phase == SWSecurityTeamPhase.Travel && team.NextWorldId is { } next && SecurityWorldPosition(next) is { } destination)
        {
            var duration = team.EtaHours - team.LegStartedAt;
            var fraction = duration <= 0 ? 1f : (float)Math.Clamp((_security!.NowHours + _securityElapsed - team.LegStartedAt) / duration, 0, 1);
            var path = _paths.FirstOrDefault(p => p.Route.From == team.WorldId && p.Route.To == next || p.Route.To == team.WorldId && p.Route.From == next);
            if (path.Route != null)
            {
                if (path.Route.From != team.WorldId) fraction = 1 - fraction;
                var index = fraction * (path.Points.Length - 1);
                var start = Math.Min((int)index, path.Points.Length - 2);
                return MapToLocal(Vector2.Lerp(path.Points[start], path.Points[start + 1], index - start));
            }
            return Vector2.Lerp(origin.Value, destination, fraction);
        }
        var peers = _security!.Teams.Where(t => t.WorldId == team.WorldId && t.Phase != SWSecurityTeamPhase.Travel).OrderBy(t => t.Id).ToArray();
        var indexAtWorld = Array.FindIndex(peers, t => t.Id == team.Id);
        return origin.Value + new Vector2(18 + indexAtWorld * 20, -18);
    }
    private bool ActivateSecurityAt(Vector2 point)
    {
        if (_security == null) return false;
        var caseHit = _security.Cases.FirstOrDefault(c => SecurityWorldPosition(c.WorldId) is { } p && Vector2.DistanceSquared(p + new Vector2(-17, -18), point) <= 81);
        if (caseHit != null) { SecurityCaseSelected?.Invoke(caseHit.Id); return true; }
        var cellHit = _security.Cells.FirstOrDefault(c => (c.Deployed || c.Burned) && SecurityWorldPosition(c.WorldId) is { } p && Vector2.DistanceSquared(p + new Vector2(-17, 18), point) <= 81);
        if (cellHit != null) { SecurityCellSelected?.Invoke(cellHit.WorldId); return true; }
        var hit = _security.Teams.Where(t => SecurityPosition(t) is { } p && Vector2.DistanceSquared(p, point) <= 100)
            .OrderBy(t => Vector2.DistanceSquared(SecurityPosition(t)!.Value, point)).FirstOrDefault();
        if (hit == null) return false;
        SecurityTeamSelected?.Invoke(hit.Id);
        return true;
    }
    private void DrawSecurity(DrawingHandleScreen handle)
    {
        if (_security == null) return;
        foreach (var dossier in _security.Cases)
        {
            if (SecurityWorldPosition(dossier.WorldId) is not { } origin) continue;
            var p = (origin + new Vector2(-17, -18)) * UIScale;
            handle.DrawRect(UIBox2.FromDimensions(p - new Vector2(6) * UIScale, new Vector2(12) * UIScale), SWConsoleStyle.Warn, false);
            handle.DrawLine(p + new Vector2(-3, 0) * UIScale, p + new Vector2(3, 0) * UIScale, SWConsoleStyle.Warn);
        }
        foreach (var cell in _security.Cells.Where(c => c.Deployed || c.Burned))
        {
            if (SecurityWorldPosition(cell.WorldId) is not { } origin) continue;
            var p = (origin + new Vector2(-17, 18)) * UIScale;
            handle.DrawCircle(p, 6 * UIScale, cell.Burned ? SWConsoleStyle.Bad : SWConsoleStyle.Good, false);
            handle.DrawLine(p + new Vector2(-4, 0) * UIScale, p + new Vector2(4, 0) * UIScale, cell.Burned ? SWConsoleStyle.Bad : SWConsoleStyle.Good);
        }
        foreach (var team in _security.Teams)
        {
            if (SecurityPosition(team) is not { } position) continue;
            var p = position * UIScale;
            var color = team.Id == _securitySelected ? SWConsoleStyle.Brass : SWConsoleStyle.Accent;
            if (team.Id == _securitySelected && team.Phase == SWSecurityTeamPhase.Travel && SecurityWorldPosition(team.NextWorldId ?? team.TargetWorldId) is { } end)
                handle.DrawLine(p, end * UIScale, SWConsoleStyle.Accent);
            handle.DrawCircle(p, 7 * UIScale, color, true);
            handle.DrawCircle(p, 10 * UIScale, color, false);
            Badge(handle, position + new Vector2(10, -7), team.Id.ToString(), color);
            if (team.Phase == SWSecurityTeamPhase.Work)
            {
                var radius = (14 + (float)(_securityAnimationSeconds % 1.5) * 8) * UIScale;
                handle.DrawCircle(p, radius, SWConsoleStyle.Warn, false);
            }
        }
    }
}
