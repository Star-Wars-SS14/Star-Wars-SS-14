using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;
using System.Collections.Generic;

namespace Content.Shared._SW.QuestBoard;

[Serializable, NetSerializable]
public sealed class SWQuestBoardState : BoundUserInterfaceState
{
    public List<SWQuestRecord> Quests;
    public SWQuestBoardState(List<SWQuestRecord> quests) { Quests = quests; }
}

[Serializable, NetSerializable]
public sealed class SWQuestCreateMessage : BoundUserInterfaceMessage
{
    public NetEntity Actor;
    public string Title = "", Description = "", Reward = "", Contact = "";
}

[Serializable, NetSerializable]
public sealed class SWQuestTakeMessage : BoundUserInterfaceMessage
{
    public NetEntity Actor;
    public int Id;
}

[Serializable, NetSerializable]
public sealed class SWQuestLeaveMessage : BoundUserInterfaceMessage
{
    public NetEntity Actor;
    public int Id;
}

[Serializable, NetSerializable]
public sealed class SWQuestCloseMessage : BoundUserInterfaceMessage
{
    public NetEntity Actor;
    public int Id;
}

[Serializable, NetSerializable]
public sealed class SWQuestCommentMessage : BoundUserInterfaceMessage
{
    public NetEntity Actor;
    public int Id;
    public string Text = "";
}

[Serializable, NetSerializable]
public sealed class SWQuestCompleteMessage : BoundUserInterfaceMessage
{
    public NetEntity Actor;
    public int Id;
}

// Pure UI feedback pings: selecting a quest or toggling a form.
[Serializable, NetSerializable]
public sealed class SWQuestSelectMessage : BoundUserInterfaceMessage
{
    public int Id;
}

[Serializable, NetSerializable]
public sealed class SWQuestUiBeepMessage : BoundUserInterfaceMessage { }