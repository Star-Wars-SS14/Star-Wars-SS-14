using Content.Shared._SW.GlobalWar.Politics;
using Robust.Client.UserInterface;

namespace Content.Client._SW.GlobalWar.UI;

public sealed class SWGlobalWarDiplomacyBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private SWGlobalWarDiplomacyMenu? _menu;

    protected override void Open()
    {
        base.Open();
        _menu = this.CreateWindow<SWGlobalWarDiplomacyMenu>();
        _menu.Send += SendMessage;
        _menu.OpenCentered();
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);
        switch (message)
        {
            case SWGlobalWarDiplomacySnapshot snapshot:
                _menu?.UpdateView(snapshot.View);
                break;
            case SWGlobalWarPoliticalResult result:
                _menu?.UpdateResult(result.Result);
                break;
            case SWGlobalWarHistoryPage page:
                _menu?.UpdateHistory(page);
                break;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _menu != null)
        {
            _menu.SaveDraft();
            _menu.Send -= SendMessage;
        }
        base.Dispose(disposing);
    }
}
