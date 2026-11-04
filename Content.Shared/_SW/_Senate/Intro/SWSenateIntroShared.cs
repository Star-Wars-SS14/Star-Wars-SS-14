using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.Shared._SW._Senate.Intro;

/// <summary>Tells a lobby client whether the next round uses the Senate preset, so the intro may be offered.</summary>
[Serializable, NetSerializable]
public sealed class SWSenateIntroStatusEvent : EntityEventArgs
{
    public bool Available;

    public SWSenateIntroStatusEvent(bool available)
    {
        Available = available;
    }
}

[CVarDefs]
public sealed class SWSenateIntroCVars
{
    /// <summary>Show the Senate intro automatically in the lobby shortly before a Senate round starts.</summary>
    public static readonly CVarDef<bool> Autoplay =
        CVarDef.Create("sw.senate_intro_autoplay", true, CVar.CLIENTONLY | CVar.ARCHIVE);
}
