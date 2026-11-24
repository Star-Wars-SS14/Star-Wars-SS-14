namespace Content.Server._SW.GlobalWar.Politics;

/// <summary>Raised once after the existing rule has initialized its world and the campaign clock is available.</summary>
[ByRefEvent]
public readonly record struct SWGlobalWarCampaignStartedEvent(EntityUid Rule, Guid Generation);

/// <summary>Stops generation-owned operations without resetting an unrelated, newer campaign.</summary>
[ByRefEvent]
public readonly record struct SWGlobalWarCampaignStoppedEvent(EntityUid Rule, Guid Generation);
