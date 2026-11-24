using Content.Shared._SW.Tutorial;
using Robust.Client.UserInterface;

namespace Content.Client._SW.Tutorial;

/// <summary>Connection-scoped network subscriptions. UIController singletons outlive the event bus on reconnect.</summary>
public sealed class SWTutorialClientSystem : EntitySystem
{
    [Dependency] private readonly IUserInterfaceManager _ui = default!;

    public override void Initialize()
    {
        base.Initialize();
        var controller = _ui.GetUIController<SWTutorialUIController>();
        SubscribeNetworkEvent<SWTutorialOfferEvent>(controller.OnOffer);
        SubscribeNetworkEvent<SWTutorialSeenEvent>(controller.OnSeen);
        SubscribeNetworkEvent<SWTutorialStateEvent>(controller.OnState);
        SubscribeNetworkEvent<SWTutorialFinishedEvent>(controller.OnFinished);
    }

    public override void Shutdown()
    {
        _ui.GetUIController<SWTutorialUIController>().ResetConnection();
        base.Shutdown();
    }
}
