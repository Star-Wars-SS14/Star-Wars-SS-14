using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Robust.Shared.Serialization;

namespace Content.Shared._SW.Species;

[DataDefinition, Serializable]
public sealed partial class SWNumberIntervalSet<T> where T : INumber<T>
{
    [DataField]
    public HashSet<SWNumberInterval<T>> Intervals;

    public SWNumberIntervalSet(T l, T r)
    {
        Intervals = new() { new SWNumberInterval<T>(l, r) };
    }

    public SWNumberIntervalSet()
    {
        Intervals = new HashSet<SWNumberInterval<T>>();
    }

    public void Add(T l, T r)
    {
        Intervals.Add(new(l, r));
    }

    public bool Contains(T value)
    {
        if (Intervals.Count == 0) return false;

        bool res = Intervals.First().Contains(value);
        foreach (var interval in Intervals)
        {
            res = res && interval.Contains(value);
        }

        return res;
    }
}

[DataDefinition, Serializable]
 public sealed partial class SWNumberInterval<T> where T : INumber<T>
{
    [DataField]
    public T Left;

    [DataField]
    public T Right;

    public SWNumberInterval(T l, T r)
    {
        Left = l;
        Right = r;
    }
    public bool Contains(T num)
    {
        return num >= Left && num <= Right;
    }
}
