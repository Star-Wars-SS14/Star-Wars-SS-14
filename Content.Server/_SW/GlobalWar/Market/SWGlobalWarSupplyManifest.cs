using Content.Shared._SW.GlobalWar;
using Content.Server._SW.GlobalWar.Politics;

namespace Content.Server._SW.GlobalWar.Market;

/// <summary>
/// References the existing items of one physical shipment part. Items remain in server-controlled custody;
/// this manifest is never inserted into raw inventory and never authorizes spawning replacement items.
/// </summary>
public sealed class SWGlobalWarSupplyManifest(Guid generation, Guid purchase, SWGlobalWarFaction origin,
    SWGlobalWarFaction owner, IReadOnlyList<SWGlobalWarSupplyManifestItem> items)
{
    public Guid Id { get; } = Guid.NewGuid();
    public Guid Generation { get; } = generation;
    public Guid Purchase { get; } = purchase;
    public SWGlobalWarFaction Origin { get; } = origin;
    public SWGlobalWarFaction Owner { get; internal set; } = owner;
    public IReadOnlyList<SWGlobalWarSupplyManifestItem> Items { get; internal set; } = items;
    public SWGlobalWarTradeCaptureEvidence? Capture { get; internal set; }
    public EntityUid? Custody { get; internal set; }
    public EntityUid? Recipient { get; internal set; }
    public bool SpaceReserved { get; internal set; }
    public bool Stored { get; internal set; }
    public bool ArrivalRecorded { get; internal set; }
    /// <summary>Uses D's shared transport budgets, but its timer cannot unload or pay for real player cargo.</summary>
    public bool AwaitingPhysical { get; internal set; }
    public Guid DestinationRevision { get; internal set; } = Guid.NewGuid();
}

/// <summary>Stable unit identity accompanies the actual entity across splits, capture and return.</summary>
public sealed record SWGlobalWarSupplyManifestItem(EntityUid Entity, Guid Unit);
