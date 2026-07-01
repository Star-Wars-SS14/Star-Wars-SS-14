using Content.Shared._SW._Senate;
using Content.Shared.Access.Systems;
using Content.Shared.GameTicking;

namespace Content.Server._SW._Senate.Core;

public sealed class SWMinistrySystem : EntitySystem
{
    [Dependency] private readonly SharedIdCardSystem _idCard = default!;

    private readonly Dictionary<string, string> _holders = new();
    private readonly Dictionary<string, EntityUid> _holderCards = new();
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

    public bool TryClaim(string ministryId, string holder, EntityUid card)
    {
        if (_holders.ContainsKey(ministryId) || IsBarred(ministryId, holder))
            return false;

        _holders[ministryId] = holder;
        _holderCards[ministryId] = card;
        return true;
    }

    public void Unclaim(string ministryId)
    {
        _holders.Remove(ministryId);
        _holderCards.Remove(ministryId);
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
