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
using static Content.Shared.Administration.Notes.AdminMessageEuiState;

namespace Content.Server._SW.Artillery
{
    public sealed class ArtilleryConsoleSystem : EntitySystem
    {
        [Dependency] private readonly SharedTransformSystem _sharedTransformSystem = default!;
        [Dependency] private readonly UserInterfaceSystem _uiSystem = default!;
        [Dependency] private readonly ChatSystem _chatSystem = default!;
        [Dependency] private readonly AccessReaderSystem _accessReaderSystem = default!;
        [Dependency] private readonly PopupSystem _popupSystem = default!;
        [Dependency] private readonly IMapManager _mapManager = default!;

        private const float UIUpdateInterval = 5.0f;

        public override void Initialize()
        {
            SubscribeLocalEvent<ArtilleryConsoleComponent, ArtilleryConsoleCallArtileryMessage>(OnCallArtillery);
            SubscribeLocalEvent<ArtilleryConsoleComponent, MapInitEvent>(OnCommunicationsConsoleMapInit);
            SubscribeLocalEvent<ArtilleryConsoleComponent, ComponentInit>((uid, comp, _) => UpdateArtilleryConsoleInterface(uid, comp));
        }

        private void OnCallArtillery(Entity<ArtilleryConsoleComponent> ent, ref ArtilleryConsoleCallArtileryMessage args)
        {
            if (!CanUse(args.Actor, ent.Owner))
            {
                _popupSystem.PopupCursor(Loc.GetString("comms-console-permission-denied"), args.Actor, PopupType.Medium);
                return;
            }
            if (!CanCall(ent.Comp))
            {
                return;
            }
            MapId id = _sharedTransformSystem.GetMapId(ent.Owner);
            var target = new MapCoordinates(new System.Numerics.Vector2(args.x, args.y), id);
            // A strike off the grid hits nothing and still costs an attack.
            if (!_mapManager.TryFindGridAt(target, out _, out _))
            {
                return;
            }
            var title = Loc.GetString(ent.Comp.Title);
            var msg = Loc.GetString(ent.Comp.AnnouncementText);
            _chatSystem.DispatchGlobalAnnouncement(msg, title, announcementSound: ent.Comp.Sound, colorOverride: ent.Comp.Color);
            Spawn(ent.Comp.ArtilleryProto, target);
            ent.Comp.CurrentDelay = ent.Comp.DelayBetweenCalls;
            ent.Comp.AttacksLeft--;
        }
        public void UpdateArtilleryConsoleInterface(EntityUid uid, ArtilleryConsoleComponent comp)
        {
            _uiSystem.SetUiState(uid, ArtilleryConsoleUiKey.Key, new ArtilleryConsoleInterfaceState
                (
                CanCall(comp),
                TimeSpan.FromSeconds(comp.CurrentDelay),
                comp.AttacksLeft)
                );
        }

        public bool CanCall(ArtilleryConsoleComponent comp)
        {
            return comp.CurrentDelay <= 0f && comp.AttacksLeft > 0;
        }

        public override void Update(float frameTime)
        {
            var query = EntityQueryEnumerator<ArtilleryConsoleComponent>();
            while (query.MoveNext(out var uid, out var comp))
            {
                // TODO refresh the UI in a less horrible way
                if (comp.CurrentDelay >= 0f)
                {
                    comp.CurrentDelay -= frameTime;
                }

                UpdateArtilleryConsoleInterface(uid, comp);
            }

            base.Update(frameTime);
        }
        private bool CanUse(EntityUid user, EntityUid console)
        {
            if (TryComp<AccessReaderComponent>(console, out var accessReaderComponent) && !HasComp<EmaggedComponent>(console))
            {
                return _accessReaderSystem.IsAllowed(user, console, accessReaderComponent);
            }
            return true;
        }
        public void OnCommunicationsConsoleMapInit(EntityUid uid, ArtilleryConsoleComponent comp, MapInitEvent args)
        {
            comp.CurrentDelay = comp.DelayBetweenCalls;
        }
    }
}
