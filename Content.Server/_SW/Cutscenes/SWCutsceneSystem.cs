using Content.Shared._SW.Cutscenes;
using Robust.Shared.Player;

namespace Content.Server._SW.Cutscenes;

public sealed class SWCutsceneSystem : EntitySystem
{
    public void PlayCutscene(string path)
    {
        RaiseNetworkEvent(new SWPlayCutsceneEvent(path), Filter.Broadcast().SendReliably());
    }
}
