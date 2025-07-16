using Content.Shared._SW;
using Content.Shared.Actions;
using Content.Shared.Hands;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.IdentityManagement;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Server._SW.SWLightsaber;

/// <inheritdoc/>
public sealed class BlockSystem : EntitySystem
{
    [Dependency] private readonly ActionContainerSystem _actionContainer = default!;
    [Dependency] private readonly SharedPopupSystem _popupSystem = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    /// <inheritdoc/>
    public override void Initialize()
    {
        SubscribeLocalEvent<BlockComponent, GetItemActionsEvent>(OnGetActions);
        SubscribeLocalEvent<BlockComponent, BlockEvent>(OnBlock);
        SubscribeLocalEvent<BlockComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(EntityUid uid, BlockComponent component, MapInitEvent args)
    {
        _actionContainer.EnsureAction(uid, ref component.BlockingActionEntity, component.BlockingAction);
    }

    private void OnGetActions(EntityUid uid, BlockComponent component, GetItemActionsEvent args)
    {
        args.AddAction(ref component.BlockingActionEntity, component.BlockingAction);
    }

    public void OnBlock(EntityUid item, BlockComponent component, BlockEvent args)
    {
        var user = args.Performer;

        if(component.IsCooldownBlocking)
            return;

        component.IsCooldownBlocking = true;

        _popupSystem.PopupEntity("Блок включен!", user, user);
        AddComp(user, new BlockEventComponent
        {
            BlockActiveTime = _timing.CurTime + TimeSpan.FromSeconds(component.BlockActiveTime),
            BlockCooldown = _timing.CurTime + TimeSpan.FromSeconds(component.BlockCooldown)
        });
        AddComp(user, new BlockCooldownComponent{});
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<BlockEventComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {

            if ((_timing.CurTime > comp.BlockActiveTime))
            {
                _popupSystem.PopupEntity("Блок отключен!", uid, uid);
                RemCompDeferred(uid, comp);
            }

            if (_timing.CurTime > comp.BlockCooldown & EntityManager.HasComponent<BlockCooldownComponent>(uid))
            {
                var block = EntityManager.GetComponent<BlockCooldownComponent>(uid);
                RemCompDeferred(uid, block);
            }
        }
    }
}
