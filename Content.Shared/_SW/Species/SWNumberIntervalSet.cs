using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Robust.Shared.Serialization;

namespace Content.Shared._SW.Species;

[DataDefinition, Serializable]
public sealed partial class SWNumberIntervalSet
{
    [DataField]
    public HashSet<SWNumberInterval> Intervals;

    public SWNumberIntervalSet(float l, float r)
    {
        Intervals = new() { new SWNumberInterval (l, r) };
    }

    public SWNumberIntervalSet()
    {
        Intervals = new HashSet<SWNumberInterval>();
    }

    public void Add(float l, float r)
    {
        Intervals.Add(new(l, r));
    }

    public bool Contains(float value)
    {
        if (Intervals.Count == 0) return false;

        bool res = Intervals.First().Contains(value);
        foreach (var interval in Intervals)
        {
            res = res || interval.Contains(value);
        }

        return res;
    }
}

[DataDefinition, Serializable]
 public sealed partial class SWNumberInterval
{
    [DataField]
    public float Left;

    [DataField]
    public float Right;

    public SWNumberInterval(float l, float r)
    {
        Left = l;
        Right = r;
    }
    public bool Contains(float num)
    {
        return num >= Left && num <= Right;
    }
}
