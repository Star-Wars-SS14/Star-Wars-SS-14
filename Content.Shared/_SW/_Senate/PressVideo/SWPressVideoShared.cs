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
    public long FeedId;
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
public sealed class SWPressVideoErrorMessage(string text) : BoundUserInterfaceMessage
{
    public string Text = text;
}

[Serializable, NetSerializable]
public sealed class SWPressVideoSpeechMessage(long feedId, string text) : BoundUserInterfaceMessage
{
    public long FeedId = feedId;
    public string Text = text;
}
