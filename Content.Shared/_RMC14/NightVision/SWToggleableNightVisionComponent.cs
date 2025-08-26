using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._RMC14.NightVision;

/// <summary>
/// Component used for toggleable night vision,
/// for example, a mob that can toggle night vision and dont need any items to be equipped.
/// It is supposed to be used with <see cref="NightVisionItemComponent">, so
/// <see cref="SWToggleableNightVisionComponent"> handles only adding action,
/// and <see cref="NightVisionItemComponent"> handles the rest
/// </summary>
[RegisterComponent]
public sealed partial class SWToggleableNightVisionComponent : Component
{
    [DataField]
    public EntProtoId? ActionId = "CMActionToggleScoutVision";
}
