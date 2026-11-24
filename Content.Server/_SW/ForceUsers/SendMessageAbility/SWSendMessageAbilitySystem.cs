using Content.Server.Actions;
using Content.Server.Chat.Managers;
using Content.Server.Players;
using Content.Shared._SW;
using Content.Shared.DoAfter;
using Content.Shared.Tag;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.ForceUsers.SendMessageAbility;
public sealed class SWSendMessageAbilitySystem : EntitySystem
{
    private static readonly ProtoId<TagPrototype> ForceUserTag = "ForceUser";

    [Dependency] private readonly ActionsSystem _actionsSystem = default!;
    [Dependency] private readonly IChatManager _chatManager = default!;
    [Dependency] private readonly ISharedPlayerManager _player = default!;
    [Dependency] private readonly TagSystem _tagSystem = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWSendMessageAbilityComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<SWSendMessageAbilityComponent, SWRequestSendMessageEvent>(OnRequest);
        SubscribeLocalEvent<SWSendMessageAbilityComponent, SWForceInfluenceDoAfterEvent>(OnInfluenceFinished);
    }

    private void OnInit(Entity<SWSendMessageAbilityComponent> ent, ref ComponentInit args)
    {
        _actionsSystem.AddAction(ent, ent.Comp.Action);
    }

    private void OnRequest(Entity<SWSendMessageAbilityComponent> ent, ref SWRequestSendMessageEvent args)
    {
        if (args.Handled || !_player.TryGetSessionByEntity(args.Target, out _))
        {
            return;
        }

        if (_tagSystem.HasTag(args.Target, ForceUserTag))
        {
            return;
        }

        // Movement, damage and distance do not interrupt preparation, but the user
        // must remain able to act. Only the user can see the preparation bar.
        var doAfter = new DoAfterArgs(EntityManager, args.Performer, ent.Comp.Delay,
            new SWForceInfluenceDoAfterEvent(), ent.Owner, target: args.Target)
        {
            Hidden = true,
            BreakOnMove = false,
            BreakOnDamage = false,
            NeedHand = false,
            RequireCanInteract = true,
            DistanceThreshold = null,
            CancelDuplicate = false,
        };

        args.Handled = _doAfter.TryStartDoAfter(doAfter);
    }

    private void OnInfluenceFinished(Entity<SWSendMessageAbilityComponent> ent, ref SWForceInfluenceDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || args.Target is not { } target ||
            _tagSystem.HasTag(target, ForceUserTag) ||
            !_player.TryGetSessionByEntity(target, out var session))
            return;

        _chatManager.DispatchServerMessage(session, Loc.GetString(ent.Comp.MessageId, ("user", Name(ent))));
        args.Handled = true;
    }
}
