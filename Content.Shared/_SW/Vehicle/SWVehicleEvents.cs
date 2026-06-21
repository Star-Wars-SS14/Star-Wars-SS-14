using Robust.Shared.Map;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW.Vehicle;

[ByRefEvent]
public sealed class SWGetGunEvent : HandledEntityEventArgs
{
    public NetEntity User;
    public NetEntity Gun = default!;
    public SWGetGunEvent(NetEntity user)
    {
        User = user;
    }
}

[ByRefEvent]
public sealed class SWGetFromCoordsShootEvent : HandledEntityEventArgs
{
    public EntityUid User;
    public EntityUid Gun;
    public EntityCoordinates Coords;
    public SWGetFromCoordsShootEvent(EntityUid user, EntityUid gun)
    {
        User = user;
        Gun = gun;
    }
}
