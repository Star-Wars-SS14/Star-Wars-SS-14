using Content.Server.DeviceNetwork.Systems;
using Content.Server.GameTicking;
using Content.Server.Shuttles.Components;
using Content.Server.Station.Events;
using Content.Shared.CCVar;
using Content.Shared.Tiles;
using Robust.Server.GameObjects;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Content.Server.Shuttles.Systems;
using Content.Server.Shuttles.Events;
using Robust.Shared.Map;
using System.Xml.Schema;

namespace Content.Server._SW.SWShuttleSystem;

public sealed partial class SWTradeShuttleSystem : EntitySystem
{
    [Dependency] private readonly SharedMapSystem _mapSystem = default!;
    [Dependency] private readonly GameTicker _ticker = default!;
    [Dependency] private readonly MapLoaderSystem _loader = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly DeviceNetworkSystem _deviceNetworkSystem = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly ShuttleSystem _shuttles = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWTradeShuttleMarkerComponent, ComponentStartup>(OnCompInit);
    }

    private void OnCompInit(Entity<SWTradeShuttleMarkerComponent> ent, ref ComponentStartup args)
    {
        // Spawn arrivals on a dummy map then dock it to the source.
        var dummpMapEntity = _mapSystem.CreateMap(out var dummyMapId);
        if (_loader.TryLoad(dummyMapId, ent.Comp.ShuttlePath.ToString(), out var shuttleUids) && shuttleUids != null)
        {
            ent.Comp.Shuttle = shuttleUids[0];
            var shuttleComp = Comp<ShuttleComponent>(ent.Comp.Shuttle);
            var tradeComp = EnsureComp<SWTradeShuttleComponent>(ent.Comp.Shuttle);
            TransformComponent xform = EnsureComp<TransformComponent>(ent.Owner);
            if (xform.MapUid != null)
            {
                var coords = new EntityCoordinates(xform.MapUid.Value, _transform.GetWorldPosition(xform));
                _shuttles.FTLToCoordinates(ent.Comp.Shuttle, shuttleComp, coords, 0f, hyperspaceTime: 3f);
            }
        }
    }
}
