using Content.Server.Actions;
using Content.Server.Chat.Managers;
using Content.Server.Players;
using Content.Shared._SW;
using Robust.Shared.Player;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.ForceUsers.SendMessageAbility;
public sealed class SWSendMessageAbilitySystem : EntitySystem
{
    [Dependency] private readonly ActionsSystem _actionsSystem = default!;
    [Dependency] private readonly IChatManager _chatManager = default!;
    [Dependency] private readonly ISharedPlayerManager _player = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWSendMessageAbilityComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<SWSendMessageAbilityComponent, SWRequestSendMessageEvent>(OnRequest);
    }

    private void OnInit(Entity<SWSendMessageAbilityComponent> ent, ref ComponentInit args)
    {
        _actionsSystem.AddAction(ent, ent.Comp.Action);
    }

    private void OnRequest(Entity<SWSendMessageAbilityComponent> ent, ref SWRequestSendMessageEvent args)
    {
        if (!_player.TryGetSessionByEntity(args.Target, out var session))
        {
            return;
        }

        _chatManager.DispatchServerMessage(session, Loc.GetString(ent.Comp.MessageId, ("user", Name(ent))));
        args.Handled = true;
    }
}
