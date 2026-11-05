using System.Numerics;
using Content.Client.Eui;
using Content.Client.UserInterface.Fragments;
using Content.Shared._SW._Senate.Holonet;
using Content.Shared.CartridgeLoader;
using Content.Shared.Eui;
using JetBrains.Annotations;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.CustomControls;

namespace Content.Client._SW._Senate.Holonet;

/// <summary>PDA fragment. The loader calls Setup on every loader state, so the view must survive repeats.</summary>
public sealed partial class SWHolonetUi : UIFragment
{
    private SWHolonetView? _view;
    private BoundUserInterface? _interface;

    public override Control GetUIFragmentRoot() => _view!;

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _interface = userInterface;
        if (_view is { Disposed: false })
            return;

        var device = IoCManager.Resolve<IEntityManager>().GetNetEntity(userInterface.Owner);
        _view = new SWHolonetView(device, true, request =>
            _interface?.SendMessage(new CartridgeUiMessage(new SWHolonetCartridgeMessage(request))));
    }

    // Holonet deliberately carries no data in the interface state shared by the PDA entity.
    public override void UpdateState(BoundUserInterfaceState state)
    {
    }
}

/// <summary>Terminal and public screen window; the view appears once the device is known.</summary>
[UsedImplicitly]
public sealed class SWHolonetEui : BaseEui
{
    private readonly DefaultWindow _window = new()
    {
        Title = Loc.GetString("sw-holonet-window-title"),
        SetSize = new Vector2(1000, 720),
        MinSize = new Vector2(760, 540),
    };

    private bool _attached;

    public SWHolonetEui()
    {
        _window.OnClose += () => SendMessage(new CloseEuiMessage());
    }

    public override void Opened()
    {
        base.Opened();
        _window.OpenCentered();
    }

    public override void Closed()
    {
        base.Closed();
        _window.Close();
    }

    public override void HandleState(EuiStateBase state)
    {
        if (_attached || state is not SWHolonetEuiState holonet)
            return;

        _attached = true;
        _window.Contents.AddChild(new SWHolonetView(holonet.Device, false,
            request => SendMessage(new SWHolonetEuiMessage(request))));
    }
}
