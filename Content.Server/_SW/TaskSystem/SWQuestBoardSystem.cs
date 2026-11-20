using Content.Server._SW.TransferSystem;
using Content.Server.GameTicking;
using Content.Shared._SW.QuestBoard;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
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
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly GameTicker _gameTicker = default!;

    private List<SWQuestRecord> _quests = new();
    private int _nextId = 1;
    private float _pollTimer;

    private Dictionary<EntityUid, TimeSpan> _lastCreate = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWQuestBoardComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<SWQuestBoardComponent, BoundUIOpenedEvent>(OnOpened);
        SubscribeLocalEvent<SWQuestBoardComponent, SWQuestCreateMessage>(OnCreate);
        SubscribeLocalEvent<SWQuestBoardComponent, SWQuestTakeMessage>(OnTake);
        SubscribeLocalEvent<SWQuestBoardComponent, SWQuestLeaveMessage>(OnLeave);
        SubscribeLocalEvent<SWQuestBoardComponent, SWQuestCloseMessage>(OnClose);
        SubscribeLocalEvent<SWQuestBoardComponent, SWQuestCommentMessage>(OnComment);
        SubscribeLocalEvent<SWQuestBoardComponent, SWQuestCompleteMessage>(OnComplete);
        SubscribeLocalEvent<SWQuestBoardComponent, SWQuestSelectMessage>(OnSelect);
        SubscribeLocalEvent<SWQuestBoardComponent, SWQuestUiBeepMessage>(OnUiBeep);
    }

    private void Beep(Entity<SWQuestBoardComponent> ent) => _audio.PlayPvs(ent.Comp.BeepSound, ent);
    private void Buzz(Entity<SWQuestBoardComponent> ent, EntityUid actor) => _audio.PlayPvs(ent.Comp.BuzzSound, actor);

    // Elapsed time since the round started, for the quest journal stamps.
    private TimeSpan RoundTime() => _gameTicker.RoundDuration();

    // Journal entry stamped with the round time.
    private SWQuestComment MakeLog(SWQuestCommentKind kind)
    {
        return new SWQuestComment { Kind = kind, RoundTime = RoundTime() };
    }

    private void OnMapInit(Entity<SWQuestBoardComponent> ent, ref MapInitEvent args)
    {
        if (_quests.Count == 0)
        {
            foreach (var starterId in ent.Comp.Starters)
            {
                if (!_prototype.TryIndex(starterId, out SWBoardQuestPrototype? starter))
                    continue;

                var giver = Loc.GetString(starter.Giver);
                var contact = string.IsNullOrEmpty(starter.Contact) ? giver : Loc.GetString(starter.Contact);
                var record = new SWQuestRecord
                {
                    Id = _nextId++,
                    Title = Loc.GetString(starter.Title),
                    Description = Loc.GetString(starter.Description),
                    Reward = Loc.GetString("sw-board-quest-reward-cash", ("amount", starter.CashReward)),
                    Contact = contact,
                    Giver = giver,
                    GiverIcon = starter.GiverIcon,
                    IsServerQuest = true,
                    ItemProto = starter.ItemProto,
                    Needed = starter.Needed,
                    CashProto = starter.CashProto,
                    CashReward = starter.CashReward,
                };
                record.Comments.Add(MakeLog(SWQuestCommentKind.Created));
                _quests.Add(record);
            }
        }
        RefreshAll();
    }

    private void OnOpened(Entity<SWQuestBoardComponent> ent, ref BoundUIOpenedEvent args)
    {
        Beep(ent);
        Push(ent);
    }

    private bool TryGetCard(EntityUid actor, out IdCardComponent card)
    {
        card = null!;
        if (!_accessReader.FindAccessItemsInventory(actor, out var items))
            return false;
        foreach (var item in items)
        {
            if (TryComp<IdCardComponent>(item, out var idCard)) { card = idCard; return true; }
            if (TryComp<PdaComponent>(item, out var pda) && pda.ContainedId != null && TryComp(pda.ContainedId, out idCard))
            { card = idCard; return true; }
        }
        return false;
    }

    private static string FormatIdentity(IdCardComponent card)
    {
        var job = string.IsNullOrEmpty(card.LocalizedJobTitle) ? "" : $", {card.LocalizedJobTitle}";
        return $"{card.FullName}{job}";
    }

    private string? ResolveMarker()
    {
        var boards = EntityQueryEnumerator<SWQuestBoardComponent>();
        while (boards.MoveNext(out _, out var boardComponent))
            return boardComponent.BoardPalletFaction;
        return null;
    }

    // Collect items on ALL board pallets into a single HashSet so an item spanning
    // multiple pallets is counted exactly once.
    private HashSet<EntityUid> ItemsOnBoardPallets(string itemProtoId)
    {
        var result = new HashSet<EntityUid>();
        var marker = ResolveMarker();
        if (marker == null) return result;

        var pallets = EntityQueryEnumerator<SWTransferPalletComponent>();
        while (pallets.MoveNext(out var palletUid, out var palletComponent))
        {
            if (palletComponent.OwnerFaction != marker) continue;
            foreach (var item in _lookup.GetEntitiesIntersecting(palletUid, LookupFlags.Dynamic | LookupFlags.Sundries))
            {
                if (result.Contains(item)) continue;
                if (TryComp<MobStateComponent>(item, out var mobState) && mobState.CurrentState == MobState.Alive) continue;
                if (TryPrototype(item, out var entityPrototype) && entityPrototype != null && entityPrototype.ID == itemProtoId)
                    result.Add(item);
            }
        }
        return result;
    }

    private void QueueDeleteEntities(IEnumerable<EntityUid> entities)
    {
        foreach (var entity in entities)
            QueueDel(entity);
    }

    // Keep live progress fresh while items sit on pallets.
    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _pollTimer += frameTime;
        if (_pollTimer < 1f) return;
        _pollTimer = 0f;

        var changed = false;
        foreach (var quest in _quests)
        {
            if (!quest.IsServerQuest || quest.Closed || string.IsNullOrEmpty(quest.ItemProto)) continue;
            var live = Math.Min(ItemsOnBoardPallets(quest.ItemProto).Count, quest.Needed);
            if (live != quest.Collected)
            {
                quest.Collected = live;
                quest.Progress = quest.Needed > 0 ? (float) live / quest.Needed : 0f;
                changed = true;
            }
        }
        if (changed) RefreshAll();
    }

    private void OnCreate(Entity<SWQuestBoardComponent> ent, ref SWQuestCreateMessage msg)
    {
        var actor = msg.Actor;
        if (!TryGetCard(actor, out var card))
        {
            _popup.PopupEntity(Loc.GetString("sw-quest-board-no-id"), actor, actor);
            Buzz(ent, actor);
            return;
        }

        if (_lastCreate.TryGetValue(actor, out var last) && (_timing.CurTime - last).TotalSeconds < ent.Comp.CreateCooldown)
            return;
        if (_quests.Count(quest => !quest.Closed) >= ent.Comp.MaxQuests) return;

        string Trim(string value) => (value ?? "").Trim();
        var title = Trim(msg.Title); var description = Trim(msg.Description);
        var reward = Trim(msg.Reward); var contact = Trim(msg.Contact);
        if (title.Length == 0 || description.Length == 0) return;
        if (description.Length > ent.Comp.MaxLength || reward.Length > ent.Comp.MaxLength || contact.Length > ent.Comp.MaxLength) return;

        _lastCreate[actor] = _timing.CurTime;
        var giver = FormatIdentity(card);
        var record = new SWQuestRecord
        {
            Id = _nextId++,
            Title = title, Description = description, Reward = reward, Contact = contact,
            Giver = giver, GiverIcon = card.JobIcon, GiverNetEntity = GetNetEntity(actor),
        };
        record.Comments.Add(MakeLog(SWQuestCommentKind.Created));
        _quests.Add(record);

        AnnounceNewQuest(title, giver);
        RefreshAll();
        _audio.PlayPvs(ent.Comp.NewTaskSound, ent);
    }

    private void OnTake(Entity<SWQuestBoardComponent> ent, ref SWQuestTakeMessage msg)
    {
        var actor = msg.Actor; var questId = msg.Id;
        var takerNetEntity = GetNetEntity(actor);
        if (!TryGetCard(actor, out var card))
        {
            _popup.PopupEntity(Loc.GetString("sw-quest-board-no-id"), actor, actor);
            Buzz(ent, actor);
            return;
        }
        var quest = _quests.FirstOrDefault(record => record.Id == questId);
        if (quest == null || quest.Closed || quest.GiverNetEntity == takerNetEntity || quest.TakersNetEntities.Contains(takerNetEntity)) return;
        quest.TakersNetEntities.Add(takerNetEntity);
        quest.Takers.Add(FormatIdentity(card));
        quest.TakerIcons.Add(card.JobIcon);
        RefreshAll();
        Beep(ent);
    }

    private void OnLeave(Entity<SWQuestBoardComponent> ent, ref SWQuestLeaveMessage msg)
    {
        var actor = msg.Actor; var questId = msg.Id;
        var takerNetEntity = GetNetEntity(actor);
        var quest = _quests.FirstOrDefault(record => record.Id == questId);
        if (quest == null) return;
        var index = quest.TakersNetEntities.IndexOf(takerNetEntity);
        if (index < 0) return;
        quest.TakersNetEntities.RemoveAt(index);
        quest.Takers.RemoveAt(index);
        if (index < quest.TakerIcons.Count) quest.TakerIcons.RemoveAt(index);
        RefreshAll();
        Beep(ent);
    }

    private void OnClose(Entity<SWQuestBoardComponent> ent, ref SWQuestCloseMessage msg)
    {
        var actor = msg.Actor; var questId = msg.Id;
        var quest = _quests.FirstOrDefault(record => record.Id == questId);
        if (quest == null || quest.Closed || quest.GiverNetEntity != GetNetEntity(actor)) return;
        quest.Closed = true;
        quest.Comments.Add(MakeLog(SWQuestCommentKind.Closed));
        RefreshAll();
        _audio.PlayPvs(ent.Comp.TaskCompleteSound, ent);
    }

    private void OnComment(Entity<SWQuestBoardComponent> ent, ref SWQuestCommentMessage msg)
    {
        var actor = msg.Actor; var questId = msg.Id;
        if (!TryGetCard(actor, out var card))
        {
            _popup.PopupEntity(Loc.GetString("sw-quest-board-no-id"), actor, actor);
            Buzz(ent, actor);
            return;
        }
        var text = (msg.Text ?? "").Trim();
        if (text.Length == 0 || text.Length > ent.Comp.MaxLength) return;
        var quest = _quests.FirstOrDefault(record => record.Id == questId);
        if (quest == null) return;
        quest.Comments.Add(new SWQuestComment
        {
            Kind = SWQuestCommentKind.Comment,
            Name = FormatIdentity(card),
            Icon = card.JobIcon,
            Text = text,
            RoundTime = RoundTime(),
        });
        RefreshAll();
        Beep(ent);
    }

    // Button-driven completion: only a registered taker may hand in a server quest.
    private void OnComplete(Entity<SWQuestBoardComponent> ent, ref SWQuestCompleteMessage msg)
    {
        var actor = msg.Actor; var questId = msg.Id;
        if (!TryGetCard(actor, out var card))
        {
            _popup.PopupEntity(Loc.GetString("sw-quest-board-no-id"), actor, actor);
            Buzz(ent, actor);
            return;
        }
        var quest = _quests.FirstOrDefault(record => record.Id == questId);
        if (quest == null || quest.Closed || !quest.IsServerQuest || string.IsNullOrEmpty(quest.ItemProto)) return;

        if (!quest.TakersNetEntities.Contains(GetNetEntity(actor)))
        {
            _popup.PopupEntity(Loc.GetString("sw-board-not-taker"), actor, actor);
            return;
        }

        var items = ItemsOnBoardPallets(quest.ItemProto);
        if (items.Count < quest.Needed) return;

        QueueDeleteEntities(items.Take(quest.Needed));

        quest.Collected = quest.Needed;
        quest.Progress = 1f;
        quest.Closed = true;
        quest.Comments.Add(MakeLog(SWQuestCommentKind.Closed));

        if (!string.IsNullOrEmpty(quest.CashProto))
        {
            var cash = Spawn(quest.CashProto, Transform(ent).Coordinates);
            if (TryComp<StackComponent>(cash, out var stack))
                _stack.SetCount(cash, quest.CashReward, stack);
        }

        RefreshAll();
        _audio.PlayPvs(ent.Comp.TaskCompleteSound, ent);
    }

    private void OnSelect(Entity<SWQuestBoardComponent> ent, ref SWQuestSelectMessage msg) => Beep(ent);
    private void OnUiBeep(Entity<SWQuestBoardComponent> ent, ref SWQuestUiBeepMessage msg) => Beep(ent);

    // Admin moderation: drop a quest entirely (e.g. rule-breaking posts).
    public bool RemoveQuest(int questId)
    {
        var quest = _quests.FirstOrDefault(record => record.Id == questId);
        if (quest == null) return false;
        _quests.Remove(quest);
        RefreshAll();
        return true;
    }

    private void RefreshAll()
    {
        var active = _quests.Any(quest => !quest.Closed && !quest.IsServerQuest);
        var query = EntityQueryEnumerator<SWQuestBoardComponent>();
        while (query.MoveNext(out var uid, out var component))
        {
            component.Quests = _quests;
            Dirty(uid, component);
            _appearance.SetData(uid, SWQuestBoardVisuals.HasActive, active);
            Push((uid, component));
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