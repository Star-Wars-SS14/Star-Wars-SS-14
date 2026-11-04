using Content.Shared._SW._Senate.Intro;

namespace Content.Client._SW._Senate.Intro;

/// <summary>Holds the server's statement that the next round is a Senate round.</summary>
public sealed class SWSenateIntroSystem : EntitySystem
{
    public bool Available { get; private set; }

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<SWSenateIntroStatusEvent>(OnStatus);
    }

    private void OnStatus(SWSenateIntroStatusEvent ev)
    {
        Available = ev.Available;
    }
}
