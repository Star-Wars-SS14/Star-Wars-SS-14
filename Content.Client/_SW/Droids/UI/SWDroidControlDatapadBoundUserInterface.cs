using Content.Shared._SW;
using Content.Shared._SW.Droids;
using Robust.Client.UserInterface;

namespace Content.Client._SW.Droids.UI;

public sealed class SWDroidControlDatapadBoundUserInterface : BoundUserInterface
{
    private SWDroidControlDatapadMenu? _menu;

    public SWDroidControlDatapadBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _menu = this.CreateWindow<SWDroidControlDatapadMenu>();
        _menu.OnJobButtonPressed += SendToggleMessage;
        _menu.OnFactionAllowButtonPressed += SendAllowMessage;
        _menu.OnFactionDisallowButtonPressed += SendDisallowMessage;
        _menu.OnTargetHumanoidsWithoutIdToggled += SendToggleTargetHumanoidsWithoutIdMessage;
        _menu.OnTargetAnimalsToggled += SendToggleTargetAnimalsMessage;
        _menu.OnFollowDatapadButtonPressed += SendFollowDatapadMessage;
        _menu.OnStopFollowDatapadButtonPressed += SendStopFollowDatapadMessage;
        _menu.OpenCenteredRight();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing || _menu == null)
            return;

        _menu.OnJobButtonPressed -= SendToggleMessage;
        _menu.OnFactionAllowButtonPressed -= SendAllowMessage;
        _menu.OnFactionDisallowButtonPressed -= SendDisallowMessage;
        _menu.OnTargetHumanoidsWithoutIdToggled -= SendToggleTargetHumanoidsWithoutIdMessage;
        _menu.OnTargetAnimalsToggled -= SendToggleTargetAnimalsMessage;
        _menu.OnFollowDatapadButtonPressed -= SendFollowDatapadMessage;
        _menu.OnStopFollowDatapadButtonPressed -= SendStopFollowDatapadMessage;
    }

    private void SendToggleMessage(string job)
    {
        SendMessage(new SWTurretJobToggleMessage(job));
    }

    private void SendAllowMessage(string faction)
    {
        SendMessage(new SWAllowFaction(faction));
    }

    private void SendDisallowMessage(string faction)
    {
        SendMessage(new SWDisallowFaction(faction));
    }

    private void SendToggleTargetHumanoidsWithoutIdMessage()
    {
        SendMessage(new SWToggleTargetHumanoidsWithoutIdMessage());
    }

    private void SendToggleTargetAnimalsMessage()
    {
        SendMessage(new SWToggleTargetAnimalsMessage());
    }

    private void SendFollowDatapadMessage(NetEntity droid)
    {
        SendMessage(new SWDroidControlDatapadFollowDroidMessage(droid));
    }

    private void SendStopFollowDatapadMessage(NetEntity droid)
    {
        SendMessage(new SWDroidControlDatapadStopFollowDroidMessage(droid));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is SWDroidControlDatapadBoundUserInterfaceState datapadState)
            _menu?.UpdateState(datapadState);
    }
}
