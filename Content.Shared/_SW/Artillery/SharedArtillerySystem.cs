
using Robust.Shared.Serialization;

namespace Content.Shared._SW.Artillery;

public sealed class SharedArtillerySystem : EntitySystem
{

}

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
    public ArtilleryConsoleInterfaceState(bool can, TimeSpan? countDownLeft)
    {
        CanCall = can;
        CountDownLeft = countDownLeft;
    }
}

[Serializable, NetSerializable]
public enum ArtilleryConsoleUiKey
{
    Key
}
