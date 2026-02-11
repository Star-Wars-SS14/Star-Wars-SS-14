using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.Lathe.MagnetPickup;

[RegisterComponent]
public sealed partial class SWMagnetUnpickupableComponent : Component
{
    public TimeSpan? DespawnTime;
}
