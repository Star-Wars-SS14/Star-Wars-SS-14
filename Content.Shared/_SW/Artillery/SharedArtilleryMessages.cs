
using Robust.Shared.Serialization;

namespace Content.Shared._SW.Artillery;

[Serializable, NetSerializable]
public sealed class ArtilleryConsoleCallArtileryMessage : BoundUserInterfaceMessage
{
    public int x;
    public int y;
    public ArtilleryConsoleCallArtileryMessage(int _x, int _y)
    {
        x = _x;
        y = _y;
    }
}
[Serializable, NetSerializable]
public sealed class ArtilleryConsoleInterfaceState : BoundUserInterfaceState
{
    public bool CanCall;
    public TimeSpan? CountDownLeft;
    public int AttackLeft;
    public ArtilleryConsoleInterfaceState(bool can, TimeSpan? countDownLeft, int attackLeft)
    {
        CanCall = can;
        CountDownLeft = countDownLeft;
        AttackLeft = attackLeft;
    }
}

[Serializable, NetSerializable]
public enum ArtilleryConsoleUiKey
{
    Key
}
