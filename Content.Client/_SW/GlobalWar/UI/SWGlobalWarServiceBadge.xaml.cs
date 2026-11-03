using Robust.Client.UserInterface;
using Robust.Client.UserInterface.XAML;

namespace Content.Client._SW.GlobalWar.UI;

public sealed partial class SWGlobalWarServiceBadge : Control
{
    public SWGlobalWarServiceBadge()
    {
        RobustXamlLoader.Load(this);
        ToolTip = Loc.GetString("sw-global-war-service-new");
    }
}
