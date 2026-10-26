using Robust.Shared.Random;

namespace Content.Server._SW._Senate.Core;

/// <summary>One scheduled arrival, with no catch-up backlog when a desk is full.</summary>
public sealed class SWSenateWorkQueue(float minHours, float maxHours, float firstMin = 0.5f, float firstMax = 1f)
{
    private float _remaining;
    private bool _first = true;
    private bool _blocked;

    public void Reset(bool first = true)
    {
        _remaining = 0f;
        _first = first;
        _blocked = false;
    }

    public bool Tick(IRobustRandom random, float hours, int pending, int capacity)
    {
        if (hours <= 0f)
            return false;
        if (_remaining <= 0f)
        {
            _remaining = random.NextFloat(_first ? firstMin : minHours, _first ? firstMax : maxHours);
            _first = false;
        }
        if (pending >= Math.Min(4, capacity))
        {
            if (!_blocked)
                _remaining = random.NextFloat(minHours, maxHours);
            _blocked = true;
            return false;
        }
        _blocked = false;
        _remaining -= hours / (pending >= 3 ? 1.5f : 1f);
        if (_remaining > 0f)
            return false;
        _remaining = random.NextFloat(minHours, maxHours);
        return true;
    }
}
