using Content.Shared._SW.GlobalWar.Politics;
using Robust.Client.UserInterface;

namespace Content.Client._SW.GlobalWar.UI;

public sealed class SWGlobalWarFinanceBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private SWGlobalWarFinanceMenu? _menu;

    protected override void Open()
    {
        base.Open();
        _menu = this.CreateWindow<SWGlobalWarFinanceMenu>();
        _menu.Send += SendMessage;
        _menu.OpenCentered();
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);
        switch (message)
        {
            case SWGlobalWarFinanceSnapshot snapshot:
                _menu?.UpdateView(snapshot.View);
                break;
            case SWGlobalWarFinanceResult result:
                _menu?.UpdateResult(result.Result);
                break;
            case SWGlobalWarFinancePage page:
                _menu?.UpdatePage(page);
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
