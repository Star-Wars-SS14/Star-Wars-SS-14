using Content.Shared.Damage;
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
    public string WeatherId = default!;

    [DataField]
    public DamageSpecifier Damage = default!;

    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(1);

    [DataField]
    public TimeSpan Duration = TimeSpan.FromMinutes(10);

    public TimeSpan NextTimeDamageApplied = TimeSpan.FromSeconds(0);
    public bool InProgress = false;
}
