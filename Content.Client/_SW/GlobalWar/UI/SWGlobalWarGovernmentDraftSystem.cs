using Content.Shared._SW.GlobalWar.Politics;
using Content.Shared.GameTicking;
using Robust.Client.Player;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Client._SW.GlobalWar.UI;

/// <summary>Own drafts and request identities only. Received government pages and balances are never cached here.</summary>
public sealed class SWGlobalWarGovernmentDraftSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _players = default!;
    private readonly Dictionary<SWGlobalWarGovernmentConsoleKind, (Scope Scope, object Draft)> _drafts = new();
    private int _attachmentEpoch;

    public readonly record struct Scope(NetUserId User, EntityUid Body, int AttachmentEpoch,
        Guid Generation, Guid Assignment, long AssignmentRevision);

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<LocalPlayerDetachedEvent>(_ => Clear());
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => Clear());
    }

    /// <summary>Call only after receiving a fresh authorized view; it invalidates the prior office's own drafts.</summary>
    public Scope? Authorize(SWGlobalWarGovernmentConsoleKind kind, Guid generation, Guid assignment, long revision)
    {
        if (_players.LocalSession is not { } session || _players.LocalEntity is not { } body ||
            generation == Guid.Empty || assignment == Guid.Empty)
        {
            _drafts.Remove(kind);
            return null;
        }
        var scope = new Scope(session.UserId, body, _attachmentEpoch, generation, assignment, revision);
        if (_drafts.TryGetValue(kind, out var old) && old.Scope != scope)
            _drafts.Remove(kind);
        return scope;
    }

    public T? Restore<T>(SWGlobalWarGovernmentConsoleKind kind, Scope scope) where T : class
    {
        return IsCurrent(scope) && _drafts.TryGetValue(kind, out var draft) && draft.Scope == scope ? draft.Draft as T : null;
    }

    public void Save(SWGlobalWarGovernmentConsoleKind kind, Scope scope, object draft)
    {
        if (IsCurrent(scope))
            _drafts[kind] = (scope, draft);
    }

    public void Revoke(SWGlobalWarGovernmentConsoleKind kind) => _drafts.Remove(kind);

    private bool IsCurrent(Scope scope) => scope.AttachmentEpoch == _attachmentEpoch &&
        scope.Body == _players.LocalEntity && scope.User == _players.LocalSession?.UserId;

    private void Clear()
    {
        _attachmentEpoch++;
        _drafts.Clear();
    }
}
