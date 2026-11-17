using Robust.Shared.Audio;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using System;
using System.Collections.Generic;

namespace Content.Shared._SW.QuestBoard;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SWQuestBoardComponent : Component
{
    [DataField, AutoNetworkedField]
    public List<SWQuestRecord> Quests = new();

    [DataField, AutoNetworkedField]
    public string NewsTicker = "Сектор стабилизирован. Правительство напоминает: сдача туш вредителей на постах продажи оплачивается из казны. Следите за доской заданий. Галактическая Империя и Республика заключили перемирие. Секта 'Серых провидцев' продолжает атаки на государственных чиновников. Синдикаты укрепляют свою власть.";

    [DataField] public SoundPathSpecifier BeepSound = new("/Audio/_SW/board/beep_board.ogg");
    [DataField] public SoundPathSpecifier BuzzSound = new("/Audio/_RMC14/Machines/buzz_two.ogg");

    [DataField] public string GovernmentName = "Правительство";
    [DataField] public string GovernmentIcon = "JobIconUnknown";

    [DataField] public string StarterTitle = "Охота на борму";
    [DataField] public string StarterDescription = "Эти звери сжирают все посевы фермеров. Убейте 5 борму и принесите их туши на поддон поста продажи, чтобы защитить урожай.";
    [DataField] public string CarcassProto = "SWMobBormu";
    [DataField] public int CarcassNeeded = 5;
    [DataField] public string CashProto = "SWEmpireCredits";
    [DataField] public int CashReward = 2000;

    [DataField] public string ParasiteTitle = "Угроза металлических паразитов";
    [DataField] public string ParasiteDescription = "Агрессивные металлические паразиты нападают на поселения и грызут обшивку с техникой. Убейте 5 паразитов и принесите их туши на поддон поста продажи. Принимаются только агрессивные особи!";
    [DataField] public string ParasiteProto = "SWMobParasitAggressiveAi";
    [DataField] public int ParasiteNeeded = 5;
    [DataField] public int ParasiteReward = 2000;

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
    public NetEntity GiverNet;
    public List<string> Takers = new();
    public List<string> TakerIcons = new();
    public List<NetEntity> TakerNets = new();
    public List<SWQuestComment> Comments = new();
    // Server-quest verification / payout, self-contained per quest.
    public string CarcassProto = "";
    public int Needed;
    public int Collected;
    public string CashProto = "";
    public int CashReward;
}