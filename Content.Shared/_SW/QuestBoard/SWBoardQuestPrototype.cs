using Content.Shared.StatusIcon;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.Shared._SW.QuestBoard;

/// <summary>
///     A round-start bulletin board quest, fully data-driven (texts live in ftl).
/// </summary>
[Prototype("swBoardQuest")]
public sealed partial class SWBoardQuestPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Title = default!;

    [DataField(required: true)]
    public LocId Description = default!;

    [DataField(required: true)]
    public LocId Giver = default!;

    [DataField]
    public ProtoId<JobIconPrototype> GiverIcon = "JobIconUnknown";

    [DataField]
    public LocId Contact = default!;

    [DataField(required: true)]
    public EntProtoId ItemProto;

    [DataField]
    public int Needed = 1;

    [DataField]
    public EntProtoId CashProto;

    [DataField]
    public int CashReward;
}