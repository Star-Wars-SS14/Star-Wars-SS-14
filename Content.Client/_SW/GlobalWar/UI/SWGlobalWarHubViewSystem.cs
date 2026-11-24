using Content.Shared._SW.GlobalWar;
using Content.Shared.GameTicking;
using Robust.Shared.Player;

namespace Content.Client._SW.GlobalWar.UI;

/// <summary>Retains exact pending work requests across window closure, scoped to a fresh authorized projection.</summary>
public sealed class SWGlobalWarHubViewSystem : EntitySystem
{
    private readonly Dictionary<NetEntity, SWGlobalWarHubDraft> _drafts = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<LocalPlayerDetachedEvent>(_ => _drafts.Clear());
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => _drafts.Clear());
    }

    public SWGlobalWarHubDraft GetDraft(NetEntity hub)
    {
        if (!_drafts.TryGetValue(hub, out var draft))
            _drafts.Add(hub, draft = new());
        return draft;
    }
}

public sealed class SWGlobalWarHubDraft
{
    public Guid Generation;
    public Guid Assignment;
    public long Revision;
    public Guid Grant;
    public Guid Order;
    public bool HeadDecision;
    public bool SelectionLost;
    public Guid LastAttempt;
    public Guid LastCancellation;
    public SWGlobalWarHubResult? Confirmed;
    public string? Feedback;
    public string Maximum = string.Empty;
    public string Minutes = "10";
    public SWGlobalWarHubCommand? Start;
    public Guid? Cancel;
    public SWGlobalWarHubGrantCommand? GrantCommand;
    public SWGlobalWarHubRevokeRequest? Revoke;
    public bool Pending => Start != null || Cancel != null || GrantCommand != null || Revoke != null;

    public void Clear()
    {
        Generation = Assignment = Grant = Order = Guid.Empty;
        Revision = 0;
        HeadDecision = false;
        SelectionLost = false;
        LastAttempt = LastCancellation = Guid.Empty;
        Confirmed = null;
        Feedback = null;
        Maximum = string.Empty;
        Minutes = "10";
        ClearPending();
    }

    public void ClearPending()
    {
        Start = null;
        Cancel = null;
        GrantCommand = null;
        Revoke = null;
    }
}
