using System.Numerics;
using Content.Client.Eui;
using Content.Client.UserInterface.Controls;
using Content.Shared._SW._Senate.Delegations;
using Content.Shared.Eui;
using JetBrains.Annotations;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._SW._Senate.Delegations;

[UsedImplicitly]
public sealed class SWDelegationEui : BaseEui
{
    private readonly FancyWindow _window = new() { Title = "Представительство", MinSize = new Vector2(400, 260) };
    private readonly RichTextLabel _text = new() { HorizontalExpand = true };
    private readonly Button _accept = new() { Text = "Согласиться" };
    private readonly Button _decline = new() { Text = "Отказаться" };
    private readonly Button _evacuate = new() { Text = "Эвакуироваться" };
    private int _version;

    public SWDelegationEui()
    {
        var body = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, VerticalExpand = true };
        var scroll = new ScrollContainer { HorizontalExpand = true, VerticalExpand = true };
        scroll.AddChild(_text);
        body.AddChild(scroll);
        body.AddChild(_accept);
        body.AddChild(_decline);
        body.AddChild(_evacuate);
        _window.ContentsContainer.AddChild(body);
        _window.OnClose += () => SendMessage(new CloseEuiMessage());
        _accept.OnPressed += _ => SendMessage(new SWDelegationActionMessage(SWDelegationAction.Accept, _version));
        _decline.OnPressed += _ => SendMessage(new SWDelegationActionMessage(SWDelegationAction.Decline, _version));
        _evacuate.OnPressed += _ => SendMessage(new SWDelegationActionMessage(SWDelegationAction.Evacuate, _version));
    }

    public override void Opened() { base.Opened(); _window.OpenCentered(); }
    public override void Closed() { base.Closed(); _window.Close(); }
    public override void HandleState(EuiStateBase state)
    {
        if (state is not SWDelegationEuiState view) return;
        _version = view.Version;
        var message = new FormattedMessage();
        message.AddText(view.Text);
        _text.SetMessage(message);
        _accept.Disabled = !view.CanAccept;
        _decline.Disabled = !view.CanDecline;
        _evacuate.Visible = view.CanEvacuate;
    }
}
