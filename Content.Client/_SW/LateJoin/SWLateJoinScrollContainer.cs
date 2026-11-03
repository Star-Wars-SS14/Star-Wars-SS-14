using System.Numerics;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._SW.LateJoin;

/// <summary>Restores a rebuilt station section only after its new scroll bounds have been measured.</summary>
public sealed class SWLateJoinScrollContainer : ScrollContainer
{
    private Vector2? _pendingScroll;

    public Vector2 SavedScroll => _pendingScroll ?? GetScrollValue();

    public void RestoreScroll(Vector2 value)
    {
        _pendingScroll = value;
        InvalidateArrange();
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        var result = base.ArrangeOverride(finalSize);
        if (_pendingScroll is not { } value)
            return result;
        _pendingScroll = null;
        SetScrollValue(value);
        // Range clamps visible axes; hidden axes must return to zero when the new content fits.
        SetScrollValue(GetScrollValue());
        return base.ArrangeOverride(finalSize);
    }
}
