using System;
using System.Collections.Generic;
using Robust.Shared.Serialization;

namespace Content.Shared._SW._Senate;

[Serializable, NetSerializable]
public sealed class SWSituationalSourceView
{
    public string Source = string.Empty;
    public string SourceName = string.Empty;
    public string? SectorId;
    public string SectorName = string.Empty;
    public float RawValue;
}

[Serializable, NetSerializable]
public sealed class SWSituationalStatView
{
    public string SubstatId = string.Empty;
    public string SubstatName = string.Empty;
    public string GlobalId = string.Empty;
    public string? SectorId;
    public float BaseValue;
    public double RawTotal;
    public double AppliedTotal;
    public double EffectiveValue;
    public List<SWSituationalSourceView> Sources = new();
}
