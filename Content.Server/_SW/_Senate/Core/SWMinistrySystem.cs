using Content.Shared.GameTicking;

namespace Content.Server._SW._Senate.Core;

public sealed class SWMinistrySystem : EntitySystem
{
    private readonly Dictionary<string, string> _holders = new();

    public IReadOnlyDictionary<string, string> Holders => _holders;

    public override void Initialize()
    {
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev) => _holders.Clear();

    public bool IsClaimed(string ministryId) => _holders.ContainsKey(ministryId);

    public string? GetHolder(string ministryId) =>
        _holders.TryGetValue(ministryId, out var holder) ? holder : null;

    public bool TryClaim(string ministryId, string holder)
    {
        if (_holders.ContainsKey(ministryId))
            return false;

        _holders[ministryId] = holder;
        return true;
    }

    public void Unclaim(string ministryId) => _holders.Remove(ministryId);
}
