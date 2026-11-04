using Content.Shared._SW.GlobalWar.Politics;
using Robust.Client.UserInterface;

namespace Content.Client._SW.GlobalWar.UI;

/// <summary>Connection-scoped network subscription. UIController singletons outlive the event bus on reconnect.</summary>
public sealed class SWGlobalWarCoachClientSystem : EntitySystem
{
    [Dependency] private readonly IUserInterfaceManager _ui = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<SWGlobalWarCoachEvent>(_ui.GetUIController<SWGlobalWarCoachUIController>().OnCoach);
        SubscribeNetworkEvent<SWGlobalWarAideEvent>(_ui.GetUIController<SWGlobalWarAideUIController>().OnAide);
    }

    public override void Shutdown()
    {
        _ui.GetUIController<SWGlobalWarCoachUIController>().ResetConnection();
        _ui.GetUIController<SWGlobalWarAideUIController>().ResetConnection();
        base.Shutdown();
    }
}
