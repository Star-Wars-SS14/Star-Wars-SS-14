using Content.Shared.Damage;
using Content.Shared.Weather;
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
    public ProtoId<WeatherPrototype> WeatherId = "Fallout";

    [DataField]
    public DamageSpecifier Damage = default!;

    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(2);

    [DataField]
    public TimeSpan Duration = TimeSpan.FromMinutes(10);

    public TimeSpan NextTimeDamageApplied = TimeSpan.FromSeconds(0);
    public bool InProgress = false;
}
