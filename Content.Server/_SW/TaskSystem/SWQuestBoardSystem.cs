using Content.Server._SW.TransferSystem;
using Content.Shared._SW.QuestBoard;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Inventory;
using Content.Shared.PDA;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Content.Server._SW.QuestBoard;

public sealed class SWQuestBoardSystem : EntitySystem
{
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedStackSystem _stack = default!;
    [Dependency] private readonly AccessReaderSystem _accessReader = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private readonly List<SWQuestRecord> _quests = new();
    private int _nextId = 1;

    private readonly Dictionary<EntityUid, TimeSpan> _lastCreate = new();
    private readonly Dictionary<EntityUid, HashSet<EntityUid>> _viewers = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWQuestBoardComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<SWQuestBoardComponent, BoundUIOpenedEvent>(OnOpened);
        SubscribeLocalEvent<SWQuestBoardComponent, BoundUIClosedEvent>(OnClosed);
        SubscribeLocalEvent<SWQuestBoardComponent, SWQuestCreateMessage>(OnCreate);
        SubscribeLocalEvent<SWQuestBoardComponent, SWQuestTakeMessage>(OnTake);
        SubscribeLocalEvent<SWQuestBoardComponent, SWQuestLeaveMessage>(OnLeave);
        SubscribeLocalEvent<SWQuestBoardComponent, SWQuestCloseMessage>(OnClose);
        SubscribeLocalEvent<SWQuestBoardComponent, SWQuestCommentMessage>(OnComment);
        SubscribeLocalEvent<SWQuestBoardComponent, SWQuestSelectMessage>(OnSelect);
        SubscribeLocalEvent<SWQuestBoardComponent, SWQuestUiBeepMessage>(OnUiBeep);
        SubscribeLocalEvent<TryTransferObject>(OnTransfer);
    }

    private void Beep(Entity<SWQuestBoardComponent> ent) => _audio.PlayPvs(ent.Comp.BeepSound, ent);
    private void Buzz(Entity<SWQuestBoardComponent> ent, EntityUid uid) => _audio.PlayPvs(ent.Comp.BuzzSound, uid);

    private void OnMapInit(Entity<SWQuestBoardComponent> ent, ref MapInitEvent args)
    {
        if (_quests.Count == 0)
        {
            _quests.Add(new SWQuestRecord
            {
                Id = _nextId++,
                Title = ent.Comp.StarterTitle,
                Description = ent.Comp.StarterDescription,
                Reward = $"{ent.Comp.CashReward} кредитов",
                Contact = ent.Comp.GovernmentName,
                Giver = ent.Comp.GovernmentName,
                GiverIcon = ent.Comp.GovernmentIcon,
                IsServerQuest = true,
                CarcassProto = ent.Comp.CarcassProto,
                Needed = ent.Comp.CarcassNeeded,
                CashProto = ent.Comp.CashProto,
                CashReward = ent.Comp.CashReward,
            });
            _quests.Add(new SWQuestRecord
            {
                Id = _nextId++,
                Title = ent.Comp.ParasiteTitle,
                Description = ent.Comp.ParasiteDescription,
                Reward = $"{ent.Comp.ParasiteReward} кредитов",
                Contact = ent.Comp.GovernmentName,
                Giver = ent.Comp.GovernmentName,
                GiverIcon = ent.Comp.GovernmentIcon,
                IsServerQuest = true,
                CarcassProto = ent.Comp.ParasiteProto,
                Needed = ent.Comp.ParasiteNeeded,
                CashProto = ent.Comp.CashProto,
                CashReward = ent.Comp.ParasiteReward,
            });
        }
        RefreshAll();
    }

    private void OnOpened(Entity<SWQuestBoardComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (!_viewers.TryGetValue(ent, out var set))
        {
            set = new HashSet<EntityUid>();
            _viewers[ent] = set;
        }
        set.Add(args.Actor);
        Beep(ent);
        Push(ent);
    }

    private void OnClosed(Entity<SWQuestBoardComponent> ent, ref BoundUIClosedEvent args)
    {
        if (_viewers.TryGetValue(ent, out var set))
            set.Remove(args.Actor);
    }

    private bool IsViewer(EntityUid board, EntityUid uid)
    {
        return _viewers.TryGetValue(board, out var set) && set.Contains(uid);
    }

    private bool TryGetCard(EntityUid uid, out IdCardComponent card)
    {
        card = null!;
        if (!_accessReader.FindAccessItemsInventory(uid, out var items))
            return false;
        foreach (var item in items)
        {
            if (TryComp<IdCardComponent>(item, out var id)) { card = id; return true; }
            if (TryComp<PdaComponent>(item, out var pda) && pda.ContainedId != null && TryComp(pda.ContainedId, out id))
            { card = id; return true; }
        }
        return false;
    }

    private static string FormatIdentity(IdCardComponent card)
    {
        var job = string.IsNullOrEmpty(card.LocalizedJobTitle) ? "" : $", {card.LocalizedJobTitle}";
        return $"{card.FullName}{job}";
    }

    private void OnCreate(Entity<SWQuestBoardComponent> ent, ref SWQuestCreateMessage msg)
    {
        var uid = GetEntity(msg.Actor);
        if (!IsViewer(ent, uid)) return;
        if (!TryGetCard(uid, out var card))
        {
            _popup.PopupEntity(Loc.GetString("sw-quest-board-no-id"), uid, uid);
            Buzz(ent, uid);
            return;
        }

        if (_lastCreate.TryGetValue(uid, out var last) && (_timing.CurTime - last).TotalSeconds < ent.Comp.CreateCooldown)
            return;
        if (_quests.Count(q => !q.Closed) >= ent.Comp.MaxQuests) return;

        string Trim(string s) => (s ?? "").Trim();
        var title = Trim(msg.Title); var desc = Trim(msg.Description);
        var reward = Trim(msg.Reward); var contact = Trim(msg.Contact);
        if (title.Length == 0 || desc.Length == 0) return;
        if (desc.Length > ent.Comp.MaxLength || reward.Length > ent.Comp.MaxLength || contact.Length > ent.Comp.MaxLength) return;

        _lastCreate[uid] = _timing.CurTime;
        var giver = FormatIdentity(card);
        _quests.Add(new SWQuestRecord
        {
            Id = _nextId++,
            Title = title, Description = desc, Reward = reward, Contact = contact,
            Giver = giver, GiverIcon = card.JobIcon, GiverNet = GetNetEntity(uid),
        });

        AnnounceNewQuest(title, giver);
        RefreshAll();
        Beep(ent);
    }

    private void OnTake(Entity<SWQuestBoardComponent> ent, ref SWQuestTakeMessage msg)
    {
        var uid = GetEntity(msg.Actor); var id = msg.Id;
        var net = GetNetEntity(uid);
        if (!IsViewer(ent, uid)) return;
        if (!TryGetCard(uid, out var card))
        {
            _popup.PopupEntity(Loc.GetString("sw-quest-board-no-id"), uid, uid);
            Buzz(ent, uid);
            return;
        }
        var q = _quests.FirstOrDefault(x => x.Id == id);
        if (q == null || q.Closed || q.GiverNet == net || q.TakerNets.Contains(net)) return;
        q.TakerNets.Add(net);
        q.Takers.Add(FormatIdentity(card));
        q.TakerIcons.Add(card.JobIcon);
        RefreshAll();
        Beep(ent);
    }

    private void OnLeave(Entity<SWQuestBoardComponent> ent, ref SWQuestLeaveMessage msg)
    {
        var uid = GetEntity(msg.Actor); var id = msg.Id;
        var net = GetNetEntity(uid);
        if (!IsViewer(ent, uid)) return;
        var q = _quests.FirstOrDefault(x => x.Id == id);
        if (q == null) return;
        var i = q.TakerNets.IndexOf(net);
        if (i < 0) return;
        q.TakerNets.RemoveAt(i);
        q.Takers.RemoveAt(i);
        if (i < q.TakerIcons.Count) q.TakerIcons.RemoveAt(i);
        RefreshAll();
        Beep(ent);
    }

    private void OnClose(Entity<SWQuestBoardComponent> ent, ref SWQuestCloseMessage msg)
    {
        var uid = GetEntity(msg.Actor); var id = msg.Id;
        if (!IsViewer(ent, uid)) return;
        var q = _quests.FirstOrDefault(x => x.Id == id);
        if (q == null || q.Closed || q.GiverNet != GetNetEntity(uid)) return;
        q.Closed = true;
        RefreshAll();
        Beep(ent);
    }

    private void OnComment(Entity<SWQuestBoardComponent> ent, ref SWQuestCommentMessage msg)
    {
        var uid = GetEntity(msg.Actor); var id = msg.Id;
        if (!IsViewer(ent, uid)) return;
        if (!TryGetCard(uid, out var card))
        {
            _popup.PopupEntity(Loc.GetString("sw-quest-board-no-id"), uid, uid);
            Buzz(ent, uid);
            return;
        }
        var text = (msg.Text ?? "").Trim();
        if (text.Length == 0 || text.Length > ent.Comp.MaxLength) return;
        var q = _quests.FirstOrDefault(x => x.Id == id);
        if (q == null || q.Closed) return;
        q.Comments.Add(new SWQuestComment { Name = FormatIdentity(card), Icon = card.JobIcon, Text = text });
        RefreshAll();
        Beep(ent);
    }

    private void OnSelect(Entity<SWQuestBoardComponent> ent, ref SWQuestSelectMessage msg) => Beep(ent);
    private void OnUiBeep(Entity<SWQuestBoardComponent> ent, ref SWQuestUiBeepMessage msg) => Beep(ent);

    private void OnTransfer(ref TryTransferObject ev)
    {
        if (ev.Handled) return;
        if (!TryPrototype(ev.PlacedUid, out var proto) || proto == null) return;

        var q = _quests.FirstOrDefault(x => x.IsServerQuest && !x.Closed
            && !string.IsNullOrEmpty(x.CarcassProto) && x.CarcassProto == proto.ID
            && x.Collected < x.Needed);
        if (q == null) return;

        q.Collected++;
        q.Progress = (float) q.Collected / q.Needed;
        ev.Handled = true;

        if (q.Collected >= q.Needed)
        {
            q.Closed = true;
            if (!string.IsNullOrEmpty(q.CashProto))
            {
                var board = EntityQueryEnumerator<SWQuestBoardComponent>();
                if (board.MoveNext(out var uid, out _))
                {
                    var cash = Spawn(q.CashProto, Transform(uid).Coordinates);
                    if (TryComp<StackComponent>(cash, out var stack))
                        _stack.SetCount(cash, q.CashReward, stack);
                }
            }
        }
        RefreshAll();
    }

    // Admin moderation: drop a quest entirely (e.g. rule-breaking posts).
    public bool RemoveQuest(int id)
    {
        var q = _quests.FirstOrDefault(x => x.Id == id);
        if (q == null) return false;
        _quests.Remove(q);
        RefreshAll();
        return true;
    }

    private void RefreshAll()
    {
        var active = _quests.Any(q => !q.Closed && !q.IsServerQuest);
        var query = EntityQueryEnumerator<SWQuestBoardComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            comp.Quests = _quests;
            Dirty(uid, comp);
            _appearance.SetData(uid, SWQuestBoardVisuals.HasActive, active);
            Push((uid, comp));
        }
    }

    private void Push(Entity<SWQuestBoardComponent> ent)
    {
        _ui.SetUiState(ent.Owner, SWQuestBoardUiKey.Key, new SWQuestBoardState(_quests));
    }

    // TODO: replace with the fork's native server-announce call (from announceui handler).
    private void AnnounceNewQuest(string title, string giver)
    {
    }
}