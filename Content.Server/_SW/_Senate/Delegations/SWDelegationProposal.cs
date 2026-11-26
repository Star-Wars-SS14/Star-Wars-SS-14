using Robust.Shared.Network;

namespace Content.Server._SW._Senate.Delegations;

/// <summary>Server-owned proposal; the caller authenticates participants before invoking it.</summary>
internal sealed class SWDelegationProposal
{
    public int Version { get; private set; } = 1;
    public NetUserId? DelegateConsent { get; private set; }
    public NetUserId? MinisterConsent { get; private set; }
    public int TargetSurcharge { get; private set; }
    public bool Agreed { get; private set; }
    public double Deadline { get; private set; }

    public SWDelegationProposal(int targetSurcharge)
    {
        TargetSurcharge = targetSurcharge;
    }

    // The caller validates permissible terms. Any modification invalidates both signatures.
    public bool ChangeTarget(int targetSurcharge)
    {
        if (Agreed || TargetSurcharge == targetSurcharge)
            return false;
        TargetSurcharge = targetSurcharge;
        Version++;
        DelegateConsent = null;
        MinisterConsent = null;
        return true;
    }

    public bool AcceptDelegate(NetUserId actor, int version)
    {
        if (Agreed || version != Version)
            return false;
        DelegateConsent = actor;
        return true;
    }

    public bool AcceptMinister(NetUserId actor, int version)
    {
        if (Agreed || version != Version)
            return false;
        MinisterConsent = actor;
        return true;
    }

    public void InvalidateTerms()
    {
        if (Agreed)
            return;
        DelegateConsent = null;
        MinisterConsent = null;
        Version++;
    }

    public void InvalidateMinister()
    {
        if (Agreed || MinisterConsent == null)
            return;
        MinisterConsent = null;
        DelegateConsent = null;
        Version++;
    }

    public bool TryConclude(NetUserId owner, NetUserId currentMinister, double now, double durationSeconds)
    {
        if (Agreed || owner == currentMinister || DelegateConsent != owner || MinisterConsent != currentMinister || durationSeconds <= 0)
            return false;
        Agreed = true;
        Deadline = now + durationSeconds;
        return true;
    }
}
