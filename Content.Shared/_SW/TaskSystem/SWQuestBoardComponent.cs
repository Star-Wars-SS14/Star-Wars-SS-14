using Robust.Shared.Audio;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using System;
using System.Collections.Generic;

namespace Content.Shared._SW.QuestBoard;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SWQuestBoardComponent : Component
{
    [DataField, AutoNetworkedField]
    public List<SWQuestRecord> Quests = new();

    [DataField]
    public LocId NewsTickerLocId = "sw-board-news-ticker";

    [DataField]
    public List<ProtoId<SWBoardQuestPrototype>> Starters = new();

    [DataField] public SoundPathSpecifier BeepSound = new("/Audio/_SW/board/beep_board.ogg");
    [DataField] public SoundPathSpecifier BuzzSound = new("/Audio/_RMC14/Machines/buzz_two.ogg");
    [DataField] public SoundPathSpecifier NewTaskSound = new("/Audio/_SW/board/new_task.ogg");
    [DataField] public SoundPathSpecifier TaskCompleteSound = new("/Audio/_SW/board/task_complete.ogg");

    [DataField] public string BoardPalletFaction = "SWQuestBoardPublic";

    [DataField] public int MaxQuests = 20;
    [DataField] public int MaxLength = 400;
    [DataField] public float CreateCooldown = 10f;
}

[Serializable, NetSerializable]
public enum SWQuestBoardUiKey : byte { Key }

[Serializable, NetSerializable]
public enum SWQuestBoardVisuals : byte { HasActive, Warning }

[Serializable, NetSerializable]
public sealed class SWQuestComment
{
    public string Name = "";
    public string Icon = "";
    public string Text = "";
}

[Serializable, NetSerializable]
public sealed class SWQuestRecord
{
    public int Id;
    public string Title = "";
    public string Description = "";
    public string Giver = "";
    public string GiverIcon = "";
    public string Reward = "";
    public string Contact = "";
    public bool Closed;
    public bool IsServerQuest;
    public float Progress;
    public NetEntity GiverNetEntity;
    public List<string> Takers = new();
    public List<string> TakerIcons = new();
    public List<NetEntity> TakersNetEntities = new();
    public List<SWQuestComment> Comments = new();
    // Server-quest verification / payout, self-contained per quest.
    public string ItemProto = "";
    public int Needed;
    public int Collected;
    public string CashProto = "";
    public int CashReward;
}