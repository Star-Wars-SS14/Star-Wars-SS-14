using Content.Server.Popups;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Chamber;
using Content.Shared.Access.Systems;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Server._SW._Senate.Core;

namespace Content.Server._SW._Senate.Debate;

public sealed class SWSenateMicSystem : EntitySystem
{
    [Dependency] private readonly SharedIdCardSystem _idCard = default!;
    [Dependency] private readonly SWSenateStateSystem _senate = default!;
    [Dependency] private readonly SWSenateFloorSystem _floor = default!;
    [Dependency] private readonly PopupSystem _popup = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<SWSenateMicComponent, ActivateInWorldEvent>(OnActivate);
    }

    private void OnActivate(Entity<SWSenateMicComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled)
            return;

        var user = args.User;
        if (!Validate(ent.Comp, user))
        {
            _popup.PopupEntity(Loc.GetString("sw-senate-floor-not-senator"), user, user, PopupType.Medium);
            args.Handled = true;
            return;
        }

        if (ent.Comp.SpeakerToggle)
        {
            var active = _floor.ToggleSpeaker(user);
            _popup.PopupEntity(Loc.GetString(active ? "sw-senate-floor-speaker-on" : "sw-senate-floor-speaker-off"), user, user, PopupType.Medium);
            args.Handled = true;
            return;
        }

        if (_floor.RequestFloor(user, ent.Comp.Priority))
            _popup.PopupEntity(Loc.GetString("sw-senate-floor-requested", ("position", _floor.GetQueuePosition(user))), user, user, PopupType.Medium);
        else
            _popup.PopupEntity(Loc.GetString("sw-senate-floor-already"), user, user, PopupType.Medium);

        args.Handled = true;
    }

    private bool Validate(SWSenateMicComponent comp, EntityUid user)
    {
        if (!_idCard.TryFindIdCard(user, out var card))
            return false;

        if (!TryComp<SWSenatePositionCardComponent>(card, out var position) || position.Position != comp.RequiredPosition)
            return false;

        return comp.RequiredPosition switch
        {
            SWSenatePosition.Speaker => _senate.Speaker == Name(user),
            SWSenatePosition.Chancellor => _senate.Chancellor == Name(user),
            _ => TryComp<SWSenateSectorComponent>(card, out var sector)
                && sector.Sector is { } claimed
                && _senate.SectorHolders.TryGetValue(claimed, out var holder)
                && holder == Name(user),
        };
    }
}
