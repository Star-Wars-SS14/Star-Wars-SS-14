using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Robust.Shared.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs;
using Robust.Shared.Player;
using Content.Shared._SW;
using Content.Shared._SW.ForceAbilities.SWStrangulateAbility;
namespace Content.Client._SW.ForceUsers;

public sealed partial class SWStrangulateOverlayController : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlayManager = default!;
    private SWStrangulateOverlay _overlay = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    public override void Initialize()
    {
        base.Initialize();
        _overlay = new SWStrangulateOverlay();
        SubscribeLocalEvent<LocalPlayerAttachedEvent>(OnPlayerAttach);
        SubscribeLocalEvent<LocalPlayerDetachedEvent>(OnPlayerDetached);
        SubscribeLocalEvent<SWUpdateStrangulateEvent>(OnUpdate);
    }

    private void OnUpdate(SWUpdateStrangulateEvent ev)
    {
        if (ev.Entity != _playerManager.LocalEntity)
            return;
        UpdateOverlay(ev.Entity);
    }

    private void OnPlayerAttach(LocalPlayerAttachedEvent args)
    {
        _overlayManager.AddOverlay(_overlay);
        UpdateOverlay(args.Entity);
    }

    private void OnPlayerDetached(LocalPlayerDetachedEvent args)
    {
        _overlayManager.RemoveOverlay(_overlay);
        _overlay.Enabled = false;
    }

    private void UpdateOverlay(EntityUid entity)
    {
        if (EntityManager.HasComponent<SWBeingStrangulatedComponent>(entity))
        {
            _overlay.Enabled = true;
        }
        else
        {
            _overlay.Enabled = false;
        }
    }
}
