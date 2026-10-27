using Content.Shared._SW._Senate;
using Content.Shared.Access.Systems;
using Content.Shared.GameTicking;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.Enums;
using Robust.Shared.Player;

namespace Content.Server._SW._Senate.Core;

public sealed class SWMinistrySystem : EntitySystem
{
    [Dependency] private readonly SharedIdCardSystem _idCard = default!;

    private readonly Dictionary<string, string> _holders = new();
    private readonly Dictionary<string, EntityUid> _holderCards = new();
    private readonly Dictionary<string, EntityUid> _holderEntities = new();
    private readonly Dictionary<string, HashSet<string>> _barred = new();

    public IReadOnlyDictionary<string, string> Holders => _holders;

    public override void Initialize()
    {
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _holders.Clear();
        _holderCards.Clear();
        _holderEntities.Clear();
        _barred.Clear();
    }

    public bool IsClaimed(string ministryId) => _holders.ContainsKey(ministryId);

    public bool IsBarred(string ministryId, string holder) =>
        _barred.TryGetValue(ministryId, out var set) && set.Contains(holder);

    public void Bar(string ministryId, string holder)
    {
        if (!_barred.TryGetValue(ministryId, out var set))
        {
            set = new HashSet<string>();
            _barred[ministryId] = set;
        }

        set.Add(holder);
    }

    public string? GetHolder(string ministryId) =>
        _holders.TryGetValue(ministryId, out var holder) ? holder : null;

    public EntityUid? GetHolderCard(string ministryId) =>
        _holderCards.TryGetValue(ministryId, out var card) ? card : null;

    /// <summary>The appointed player is connected and controlling their living minister.
    /// The ID can remain inserted in a console without suspending the ministry.</summary>
    public bool IsHolderActive(string ministryId) =>
        _holders.ContainsKey(ministryId)
        && _holderEntities.TryGetValue(ministryId, out var entity)
        && TryComp<ActorComponent>(entity, out var actor)
        && actor.PlayerSession.Status == SessionStatus.InGame
        && actor.PlayerSession.AttachedEntity == entity
        && TryComp<MobStateComponent>(entity, out var mob)
        && mob.CurrentState == MobState.Alive;

    public bool IsActiveHolder(string ministryId, EntityUid actor) =>
        _holderEntities.TryGetValue(ministryId, out var holder) && holder == actor && IsHolderActive(ministryId);

    public bool TryClaim(string ministryId, string holder, EntityUid card, EntityUid? actor = null)
    {
        if (_holders.ContainsKey(ministryId) || IsBarred(ministryId, holder))
            return false;

        _holders[ministryId] = holder;
        _holderCards[ministryId] = card;
        if (actor is { } entity)
            _holderEntities[ministryId] = entity;
        return true;
    }

    public void Unclaim(string ministryId)
    {
        _holders.Remove(ministryId);
        _holderCards.Remove(ministryId);
        _holderEntities.Remove(ministryId);
    }

    public bool TryTransfer(string fromId, string toId, EntityUid card)
    {
        if (fromId == toId
            || !_holders.TryGetValue(fromId, out var holder)
            || !_holderCards.TryGetValue(fromId, out var heldCard)
            || heldCard != card
            || _holders.ContainsKey(toId)
            || IsBarred(toId, holder))
            return false;

        _holders.Remove(fromId);
        _holderCards.Remove(fromId);
        _holders[toId] = holder;
        _holderCards[toId] = card;
        if (_holderEntities.Remove(fromId, out var entity))
            _holderEntities[toId] = entity;
        return true;
    }

    public void ReconcileCard(EntityUid card)
    {
        if (!TryComp<SWMinistryComponent>(card, out var comp) || comp.Ministry is not { } id)
            return;

        if (!_holderCards.TryGetValue(id, out var held) || held != card)
        {
            comp.Ministry = null;
            _idCard.TryChangeJobTitle(card, Loc.GetString("sw-minister-base"));
        }
    }
}
