using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Shared.CCVar;
using Content.Client._SW._Senate.Consoles.MinistryWork;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Economy;
using Content.Shared._SW._Senate.Sectors;
using Robust.Client.Graphics;
using Robust.Shared.Maths;

namespace Content.Client._SW._Senate.Consoles.Statistics;

public sealed partial class SWGalacticMapControl
{
    private bool _medicalVisible;
    private double _medicalHour = double.NaN;
    private double _medicalElapsed;
    private string? _selectedMedicalProject;
    public event Action<SWHealthSupplyFlowView>? MedicalSupplySelected;
    public bool MedicalSupplyVisible => _medicalVisible;
    public void SetMedicalSupplyVisible(bool visible)
    {
        if (_medicalVisible == visible) return;
        _medicalVisible = visible;
        if (_economy != null) RebuildTraffic();
        UpdateHover();
    }
    private void UpdateMedicalSupply()
    {
        var hour = _economy?.Hour ?? double.NaN;
        if (_medicalHour != hour)
        {
            // A late publication must not rewind a shipment already interpolated towards it.
            // A clock reset starts a new epoch; never carry an old round's interpolation over.
            var displayed = _medicalHour + _medicalElapsed;
            _medicalElapsed = double.IsFinite(hour) && hour >= _medicalHour
                ? Math.Clamp(displayed - hour, 0, .25) : 0;
        }
        _medicalHour = hour;
    }
    private void AdvanceMedicalSupply(float seconds)
    {
        if (!VisibleInTree || !_medicalVisible || _economy is not { Ready: true, Paused: false } view
            || view.SupplyRealSecondsPerHour <= 0 || !float.IsFinite(seconds) || seconds <= 0
            || _reduceTrafficMotion || _configuration.GetCVar(CCVars.ReducedMotion)) return;
        // Only one accepted economic step may be interpolated. Unbounded extrapolation
        // used to reach the destination early and jump back on every delayed snapshot.
        _medicalElapsed = Math.Min(.25, _medicalElapsed + Math.Min(seconds, .25f) / view.SupplyRealSecondsPerHour);
    }
    private (SWGalacticMapRoute Route, Vector2[] Points) MedicalPath(SWHealthSupplyFlowView flow) =>
        _paths.FirstOrDefault(p => SWTransitLinkPrototype.LinkId(p.Route.From, p.Route.To) == flow.EdgeId
            || p.Route.From == flow.From && p.Route.To == flow.To || p.Route.From == flow.To && p.Route.To == flow.From);
    public Vector2? MedicalSupplyPosition(string id)
    {
        var flow = _economy?.MedicalSupplyFlows.FirstOrDefault(f => f.Id == id);
        if (!_medicalVisible || flow == null || flow.Quantity <= 0 || !double.IsFinite(flow.DepartAtHours)
            || !double.IsFinite(flow.ArriveAtHours) || flow.ArriveAtHours <= flow.DepartAtHours
            || _medicalHour < flow.DepartAtHours || _medicalHour >= flow.ArriveAtHours) return null;
        if (MedicalSupplyRoutePosition(flow) is not { } point) return null;
        if (point.X < 0 || point.Y < 0 || point.X > Width || point.Y > Height) return null;
        // Keep the cross clear of planet nodes, measured labels and crisis badges.
        LayoutCrisisMarkers();
        bool Clear(Vector2 candidate)
        {
            var bounds = UIBox2.FromDimensions(candidate - new Vector2(10), new Vector2(20));
            return bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= Width && bounds.Bottom <= Height
                && !_map.Worlds.Any(w => Vector2.DistanceSquared(MapToLocal(w.Position), candidate) < 18 * 18)
                && !_labels.Any(l => l.Box.Intersects(bounds)) && !_crisisMarkers.Any(m => m.Bounds.Intersects(bounds));
        }
        if (Clear(point)) return point;
        // Dense galaxy views need more than a few hand-picked offsets. Search the nearest
        // clear ring first; a leader preserves the actual position on the transport route.
        for (var radius = 18; radius <= 126; radius += 18)
        for (var direction = 0; direction < 16; direction++)
        {
            var angle = direction * MathF.Tau / 16;
            var candidate = point + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
            if (Clear(candidate)) return candidate;
        }
        // A completely obstructed local cluster must not turn a convoy into a crisis hit target
        // or move it to an unrelated part of the map. Work stays bounded to 112 offsets.
        return null;
    }
    private Vector2? MedicalSupplyRoutePosition(SWHealthSupplyFlowView flow)
    {
        var path = MedicalPath(flow);
        if (path.Route == null || path.Points.Length < 2) return null;
        var progress = (float)Math.Clamp((_medicalHour + _medicalElapsed - flow.DepartAtHours)
            / (flow.ArriveAtHours - flow.DepartAtHours), 0, 1);
        if (path.Route.From != flow.From) progress = 1 - progress;
        var index = progress * (path.Points.Length - 1);
        var start = Math.Min((int)index, path.Points.Length - 2);
        return MapToLocal(Vector2.Lerp(path.Points[start], path.Points[start + 1], index - start));
    }
    private SWHealthSupplyFlowView? HitMedicalSupply(Vector2 point) => !_medicalVisible ? null :
        _economy?.MedicalSupplyFlows.FirstOrDefault(f => MedicalSupplyPosition(f.Id) is { } p
            && Vector2.DistanceSquared(p, point) <= 100);
    private bool ActivateMedicalSupply(Vector2 point)
    {
        if (HitMedicalSupply(point) is not { } flow) return false;
        _selectedMedicalProject = flow.ProjectId;
        MedicalSupplySelected?.Invoke(flow);
        return true;
    }
    private string? MedicalSupplyToolTip(Vector2 point)
    {
        if (HitMedicalSupply(point) is not { } f) return null;
        return $"{MedicalKindName(f.Kind, f.Payload)}{MedicalStrainName(f.StrainId)}\nОткуда: {MedicalWorldName(f.From)}\nКуда: {MedicalWorldName(f.To)}\n{f.GoodName}: {f.Quantity:0.###}\n" +
            $"Получатель: {MedicalWorldName(f.TargetWorldId)}\n" + MedicalDeliveryEta(f, _medicalHour + _medicalElapsed, _economy?.Paused ?? false);
    }
    public static string MedicalDeliveryEta(SWHealthSupplyFlowView flow, double hour, bool paused) => SWHealthDeliveryText.Flow(flow, hour, paused);
    private string MedicalWorldName(string id) => _worlds.GetValueOrDefault(id)?.Name
        ?? _crisisWorlds.FirstOrDefault(w => w.Id == id)?.Name ?? "Неизвестный мир";
    public static string MedicalStrainName(string strain)
    {
        var last = strain.LastIndexOf(':');
        return int.TryParse(last < 0 ? strain : strain[(last + 1)..], out var number) ? $"; штамм {number + 1}" : "";
    }
    public static string MedicalKindName(string kind, bool payload = false) => kind switch
    {
        "Research" => "Снабжение исследований", "Relief" => "Медицинская помощь",
        "VaccineProduction" => payload ? "Адресная вакцина" : "Материалы для производства вакцины",
        "Prevention" => "Профилактика", "RegenerativeCare" => "Регенеративная помощь",
        "ProstheticCare" => "Протезирование", _ => "Медицинские материалы",
    };
    private void DrawMedicalSupply(DrawingHandleScreen handle)
    {
        if (!_medicalVisible || _economy == null) return;
        var color = SWConsoleStyle.Good;
        foreach (var f in _economy.MedicalSupplyFlows)
        {
            if (MedicalSupplyPosition(f.Id) is not { } point) continue;
            var path = MedicalPath(f);
            for (var i = 1; i < path.Points.Length; i++)
                Stroke(handle, MapToLocal(path.Points[i - 1]) * UIScale, MapToLocal(path.Points[i]) * UIScale,
                    color.WithAlpha(f.ProjectId == _selectedMedicalProject ? 1 : .6f), f.ProjectId == _selectedMedicalProject ? 3 : 1.5f);
            if (MedicalSupplyRoutePosition(f) is { } anchor && Vector2.DistanceSquared(anchor, point) > 12 * 12)
                handle.DrawLine(anchor * UIScale, point * UIScale, color.WithAlpha(.4f));
            var p = point * UIScale;
            handle.DrawCircle(p, 8 * UIScale, SWConsoleStyle.Navigation);
            handle.DrawLine(p - new Vector2(5, 0) * UIScale, p + new Vector2(5, 0) * UIScale, color);
            handle.DrawLine(p - new Vector2(0, 5) * UIScale, p + new Vector2(0, 5) * UIScale, color);
        }
    }
}

