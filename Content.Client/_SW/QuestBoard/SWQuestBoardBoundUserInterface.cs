using Content.Shared._SW.QuestBoard;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;

namespace Content.Client._SW.QuestBoard;

public sealed class SWQuestBoardBoundUserInterface : BoundUserInterface
{
    private SWQuestBoardMenu? _menu;

    public SWQuestBoardBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey) { }

    private NetEntity LocalActor()
    {
        var local = IoCManager.Resolve<IPlayerManager>().LocalEntity ?? EntityUid.Invalid;
        return IoCManager.Resolve<IEntityManager>().GetNetEntity(local);
    }

    protected override void Open()
    {
        base.Open();
        _menu = this.CreateWindow<SWQuestBoardMenu>();
        _menu.OnTake += id => SendMessage(new SWQuestTakeMessage { Actor = LocalActor(), Id = id });
        _menu.OnLeave += id => SendMessage(new SWQuestLeaveMessage { Actor = LocalActor(), Id = id });
        _menu.OnClose += id => SendMessage(new SWQuestCloseMessage { Actor = LocalActor(), Id = id });
        _menu.OnComment += (id, text) => SendMessage(new SWQuestCommentMessage { Actor = LocalActor(), Id = id, Text = text });
        _menu.OnComplete += id => SendMessage(new SWQuestCompleteMessage { Actor = LocalActor(), Id = id });
        _menu.OnSelect += id => SendMessage(new SWQuestSelectMessage { Id = id });
        _menu.OnUiBeep += () => SendMessage(new SWQuestUiBeepMessage());
        _menu.OnSubmit += (t, d, r, c) => SendMessage(new SWQuestCreateMessage
        { Actor = LocalActor(), Title = t, Description = d, Reward = r, Contact = c });
        _menu.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is SWQuestBoardState s)
            _menu?.Populate(s.Quests);
        if (EntMan.TryGetComponent<SWQuestBoardComponent>(Owner, out var comp))
            _menu?.SetTicker(comp.NewsTicker);
    }
}