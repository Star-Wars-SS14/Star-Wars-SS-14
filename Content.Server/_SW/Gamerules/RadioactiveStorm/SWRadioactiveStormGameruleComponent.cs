using Content.Shared.Damage;
using Content.Shared.Weather;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.Gamerules.RadioactiveStorm;

[RegisterComponent]
public sealed partial class SWRadioactiveStormGameruleComponent : Component
{
    [DataField]
    public LocId StartAnnouncementId = "sw-radioactive-storm-start";

    [DataField]
    public LocId EndAnnouncementId = "sw-radioactive-storm-end";

    [DataField]
    public ProtoId<WeatherPrototype> WeatherId = "Fallout";

    [DataField]
    public DamageSpecifier Damage = default!;

    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(1);

    [DataField]
    public TimeSpan Duration = TimeSpan.FromMinutes(10);

    [DataField]
    public TimeSpan DelayBetweenTriggerAndStarting = TimeSpan.FromMinutes(3);

    public TimeSpan? TimeOfStart = null;
    public TimeSpan? TimeOfEnd = null;

    public TimeSpan NextTimeDamageApplied = TimeSpan.FromSeconds(0);

    public List<MapId> AffectedMaps = new List<MapId>();

    public bool InProgress = false;

    public bool IsTriggered = false;

    public Color Color = Color.Green;
}
