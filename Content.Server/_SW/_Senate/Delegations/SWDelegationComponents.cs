using Robust.Shared.Network;

namespace Content.Server._SW._Senate.Delegations;

/// <summary>Arrival and departure location for Senate representatives.</summary>
[RegisterComponent]
public sealed partial class SWDelegationPointComponent : Component
{
    [DataField] public float Range = 3f;
    [DataField] public float CheckInterval = 60f;
    [DataField] public float Cooldown = 600f;
    [DataField] public float ObligationDuration = 600f;
    [DataField] public float OfferDuration = 120f;
    [DataField] public float DisconnectGrace = 120f;
    [DataField] public float VisitDuration = 1200f;
}

[RegisterComponent]
public sealed partial class SWDelegationMemberComponent : Component
{
    public NetUserId? Owner;
    public int VisitId;
    public bool Retired;
    public float Grace;
    public float RemainingGrace;
}
