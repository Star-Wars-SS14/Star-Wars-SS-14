using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.Gamerules.RefillOre;

[RegisterComponent]
public sealed partial class SWRespawnEntitiesGameruleComponent : Component
{
    [DataField]
    public TimeSpan Delay = TimeSpan.FromMinutes(10);

    [DataField]
    public TimeSpan DelayBeforeSpawn = TimeSpan.FromSeconds(30);

    [DataField]
    public LocId AnnouncementWarningId = "sw-announce-mine-collapse-warning";

    [DataField]
    public LocId AnnouncementId = "sw-announce-mine-collapsed";

    [DataField]
    public Color AnnounceColor = Color.SaddleBrown;

    public TimeSpan NextSpawn = TimeSpan.FromSeconds(0);
    public TimeSpan NextWarning = TimeSpan.FromSeconds(0);

    public bool WarningDispatched = false;
}
