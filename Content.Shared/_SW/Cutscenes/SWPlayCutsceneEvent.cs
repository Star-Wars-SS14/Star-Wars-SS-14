using Robust.Shared.Serialization;

namespace Content.Shared._SW.Cutscenes;

[Serializable, NetSerializable]
public sealed class SWPlayCutsceneEvent : EntityEventArgs
{
    public string Path { get; }

    public SWPlayCutsceneEvent(string path)
    {
        Path = path;
    }
}
