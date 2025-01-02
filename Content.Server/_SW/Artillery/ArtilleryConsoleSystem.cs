using System.Globalization;
using Content.Server.Access.Systems;
using Content.Server.Administration.Logs;
using Content.Server.AlertLevel;
using Content.Server.Chat.Systems;
using Content.Server.DeviceNetwork;
using Content.Server.DeviceNetwork.Components;
using Content.Server.DeviceNetwork.Systems;
using Content.Server.Interaction;
using Content.Server.Popups;
using Content.Server.RoundEnd;
using Content.Server.Screens;
using Content.Server.Screens.Components;
using Content.Server.Shuttles.Systems;
using Content.Server.Station.Systems;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.CCVar;
using Content.Shared.Chat;
using Content.Shared.Communications;
using Content.Shared.Database;
using Content.Shared.DeviceNetwork;
using Content.Shared.Emag.Components;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Content.Shared._SW.Artillery;

namespace Content.Server._SW.Artillery
{
    public sealed class ArtilleryConsoleSystem : EntitySystem
    {
        [Dependency] private readonly SharedTransformSystem _sharedTransformSystem = default!;
        [Dependency] private readonly UserInterfaceSystem _uiSystem = default!;

        private const float UIUpdateInterval = 5.0f;

        public override void Initialize()
        {
            SubscribeLocalEvent<ArtilleryConsoleComponent, ArtilleryConsoleCallArtileryMessage>(OnCallArtillery);
            SubscribeLocalEvent<ArtilleryConsoleComponent, MapInitEvent>(OnCommunicationsConsoleMapInit);
            SubscribeLocalEvent<ArtilleryConsoleComponent, ComponentInit>((uid, comp, _) => UpdateCommsConsoleInterface(uid, comp));
        }

        private void OnCallArtillery(Entity<ArtilleryConsoleComponent> ent, ref ArtilleryConsoleCallArtileryMessage args)
        {
            MapId id = _sharedTransformSystem.GetMapId(ent.Owner);
            Spawn(ent.Comp.ArtilleryProto, new MapCoordinates(new System.Numerics.Vector2(args.x, args.y), id));
        }
        public void UpdateCommsConsoleInterface(EntityUid uid, ArtilleryConsoleComponent comp)
        {
        _uiSystem.SetUiState(uid, ArtilleryConsoleUiKey.Key, new ArtilleryConsoleInterfaceState());
        }

        public override void Update(float frameTime)
        {
            var query = EntityQueryEnumerator<ArtilleryConsoleComponent>();
            while (query.MoveNext(out var uid, out var comp))
            {
                //// TODO refresh the UI in a less horrible way
                //if (comp.AnnouncementCooldownRemaining >= 0f)
                //{
                //    comp.AnnouncementCooldownRemaining -= frameTime;
                //}

                //comp.UIUpdateAccumulator += frameTime;

                //if (comp.UIUpdateAccumulator < UIUpdateInterval)
                //    continue;

                //comp.UIUpdateAccumulator -= UIUpdateInterval;

                //if (_uiSystem.IsUiOpen(uid, CommunicationsConsoleUiKey.Key))
                //    UpdateCommsConsoleInterface(uid, comp);
            }

            base.Update(frameTime);
        }

        public void OnCommunicationsConsoleMapInit(EntityUid uid, ArtilleryConsoleComponent comp, MapInitEvent args)
        {

        }
    }
}
