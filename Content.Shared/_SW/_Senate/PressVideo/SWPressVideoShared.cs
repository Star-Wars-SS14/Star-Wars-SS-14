using Robust.Shared.Serialization;

namespace Content.Shared._SW._Senate.PressVideo;

[Serializable, NetSerializable]
public enum SWPressCameraUiKey : byte { Key }
[Serializable, NetSerializable]
public enum SWPressViewerUiKey : byte { Key }
[Serializable, NetSerializable]
public sealed class SWPressVideoState : BoundUserInterfaceState
{
    public NetEntity? Camera;
    public bool IsSource;
    public string SoundStatus = string.Empty;
    public string Topic = string.Empty;
    public string Presenter = string.Empty;
    public string Edition = string.Empty;
}
[Serializable, NetSerializable]
public sealed class SWPressStartMessage(string topic) : BoundUserInterfaceMessage
{
    public string Topic = topic;
}
[Serializable, NetSerializable]
public sealed class SWPressStopMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class SWPressSpeakerConsentMessage(bool enabled) : BoundUserInterfaceMessage
{
    public bool Enabled = enabled;
}

[Serializable, NetSerializable]
public sealed class SWPressViewerAudioMessage(bool enabled, long token) : BoundUserInterfaceMessage
{
    public bool Enabled = enabled;
    public long Token = token;
}
[Serializable, NetSerializable]
public sealed class SWPressAudioPlayEvent(byte[] data, long token) : EntityEventArgs
{
    public byte[] Data = data;
    public long Token = token;
}

[Serializable, NetSerializable]
public sealed class SWPressAudioStopEvent : EntityEventArgs;
[Serializable, NetSerializable]
public sealed class SWPressSpeakerConsentResult(bool requested, bool enabled) : BoundUserInterfaceMessage
{
    public bool Requested = requested;
    public bool Enabled = enabled;
}