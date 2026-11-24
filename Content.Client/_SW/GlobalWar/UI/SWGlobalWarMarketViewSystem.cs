using Content.Shared.GameTicking;
using Robust.Client.Player;
using Robust.Shared.Player;

namespace Content.Client._SW.GlobalWar.UI;

/// <summary>Invalidates addressed economic views when the player leaves the body or the round ends.</summary>
public sealed class SWGlobalWarMarketViewSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _players = default!;
    public event Action? Invalidated;
    private readonly Dictionary<NetEntity, SWGlobalWarFlowDraft> _drafts = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<LocalPlayerDetachedEvent>(_ => Invalidate());
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => Invalidate());
    }

    private void Invalidate()
    {
        foreach (var draft in _drafts.Values)
            draft.Clear();
        _drafts.Clear();
        Invalidated?.Invoke();
    }

    public SWGlobalWarFlowDraft GetDraft(NetEntity console)
    {
        if (!_drafts.TryGetValue(console, out var draft))
            _drafts.Add(console, draft = new());
        return draft;
    }

    public bool IsCurrentViewer(NetEntity viewer)
    {
        return _players.LocalEntity is { } body && GetNetEntity(body) == viewer;
    }
}
