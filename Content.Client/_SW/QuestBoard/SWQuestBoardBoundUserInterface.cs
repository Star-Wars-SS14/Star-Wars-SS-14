using Content.Shared._SW.QuestBoard;
using Robust.Client.UserInterface;

namespace Content.Client._SW.QuestBoard;

public sealed class SWQuestBoardBoundUserInterface : BoundUserInterface
{
    private SWQuestBoardMenu? _menu;

    public SWQuestBoardBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey) { }

    protected override void Open()
    {
        base.Open();
        _menu = this.CreateWindow<SWQuestBoardMenu>();
        _menu.OnTake += questId => SendMessage(new SWQuestTakeMessage { Id = questId });
        _menu.OnLeave += questId => SendMessage(new SWQuestLeaveMessage { Id = questId });
        _menu.OnClose += questId => SendMessage(new SWQuestCloseMessage { Id = questId });
        _menu.OnComment += (questId, text) => SendMessage(new SWQuestCommentMessage { Id = questId, Text = text });
        _menu.OnComplete += questId => SendMessage(new SWQuestCompleteMessage { Id = questId });
        _menu.OnSelect += questId => SendMessage(new SWQuestSelectMessage { Id = questId });
        _menu.OnUiBeep += () => SendMessage(new SWQuestUiBeepMessage());
        _menu.OnSubmit += (title, description, reward, contact) => SendMessage(new SWQuestCreateMessage
        { Title = title, Description = description, Reward = reward, Contact = contact });
        _menu.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is SWQuestBoardState boardState)
            _menu?.Populate(boardState.Quests);
        if (EntMan.TryGetComponent<SWQuestBoardComponent>(Owner, out var component))
            _menu?.SetTicker(Loc.GetString(component.NewsTickerLocId));
    }
}