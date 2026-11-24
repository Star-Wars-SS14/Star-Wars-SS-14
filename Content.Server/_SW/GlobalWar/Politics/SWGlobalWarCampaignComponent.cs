using Content.Shared._SW.GlobalWar.Combat;
using Content.Shared._SW.GlobalWar.Politics;

namespace Content.Server._SW.GlobalWar.Politics;

/// <summary>Political campaign configuration and active-time lifecycle on the existing GlobalWar rule.</summary>
[RegisterComponent, Access(typeof(SWGlobalWarCampaignSystem))]
public sealed partial class SWGlobalWarCampaignComponent : Component
{
    /// <summary>Active campaign time at which neutral funding closes.</summary>
    [DataField]
    public TimeSpan NeutralFundingDuration = TimeSpan.FromMinutes(30);

    /// <summary>Earliest active campaign time for a declaration of war.</summary>
    [DataField]
    public TimeSpan PeaceDuration = TimeSpan.FromMinutes(60);

    /// <summary>Uncontested political annexation time for an unresolved neutral sector.</summary>
    [DataField]
    public TimeSpan AnnexationDuration = TimeSpan.FromSeconds(600);

    /// <summary>Only the head of state has an automatic disconnect reservation deadline.</summary>
    [DataField]
    public TimeSpan LeaderDisconnectReserve = TimeSpan.FromSeconds(120);

    /// <summary>Time allowed for existing forces to withdraw after their access is revoked.</summary>
    [DataField]
    public TimeSpan WithdrawalDuration = TimeSpan.FromSeconds(120);

    /// <summary>Time to transfer the remaining territory after an accepted capitulation.</summary>
    [DataField]
    public TimeSpan CapitulationDuration = TimeSpan.FromSeconds(300);

    /// <summary>Continuous absence of a faction's connected participants before a non-victory ending.</summary>
    [DataField]
    public TimeSpan ParticipantAbsenceDuration = TimeSpan.FromMinutes(10);

    /// <summary>Limit for a running campaign; an earlier accepted capitulation may finish afterwards.</summary>
    [DataField]
    public TimeSpan MaximumDuration = TimeSpan.FromMinutes(180);

    /// <summary>At the limit the side holding more orbits and surfaces wins. A tie, or false, ends with no winner.</summary>
    [DataField]
    public bool TimeLimitVictory;

    /// <summary>How long before the limit the score is announced, when <see cref="TimeLimitVictory"/> is set.</summary>
    [DataField]
    public TimeSpan TimeLimitWarning = TimeSpan.FromMinutes(10);

    /// <summary>Ship classes this campaign does not sell. Their maps, ports and reward grants stay as they are.</summary>
    [DataField]
    public HashSet<SWGlobalWarShipClass> DisabledShipClasses = new();

    public Guid Generation;
    public TimeSpan StartedAt;
    public TimeSpan? StoppedElapsed;
    public SWGlobalWarCampaignPhase Phase;
}
