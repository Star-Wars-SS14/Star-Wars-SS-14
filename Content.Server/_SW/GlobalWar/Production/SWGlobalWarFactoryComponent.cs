using Content.Shared._SW.GlobalWar;

namespace Content.Server._SW.GlobalWar.Production;

/// <summary>A physical endpoint of the campaign inventory. It has no parallel item or money balance.</summary>
[RegisterComponent]
public sealed partial class SWGlobalWarFactoryComponent : Component
{
    /// <summary>Whether this endpoint runs the campaign production consumer.</summary>
    [DataField] public bool Factory;

    /// <summary>Whether this endpoint accepts paid military procurement and issues equipment.</summary>
    [DataField] public bool Arsenal;

    /// <summary>Whether this endpoint recruits the current civilian without spawning equipment.</summary>
    [DataField] public bool Recruitment;

    /// <summary>Extracts the separate finite physical ore deposit at six units per powered minute.</summary>
    [DataField] public bool Mining;

    /// <summary>Starts the base supply program when a new campaign binds this physical line.</summary>
    [DataField] public bool AutoStart = true;

    /// <summary>The initial production line; changing it never changes an already reserved recipe.</summary>
    [DataField] public SWGlobalWarSupplyKind Kind = SWGlobalWarSupplyKind.T1;

    /// <summary>Further base program lines the same machine serves from the start, one batch at a time.</summary>
    [DataField] public List<SWGlobalWarSupplyKind> ExtraKinds = new();

    public Guid Generation;
    public bool Enabled;
    public TimeSpan LastUpdate;
    public SWGlobalWarLocationLease? WorkLease;
    public TimeSpan MiningTime;
    public EntityUid? MiningOutput;
    public EntityUid? MiningEscrow;
    public string Feedback = string.Empty;
    public readonly Dictionary<Guid, SWGlobalWarPhysicalProduction> Work = new();
}

/// <summary>Retains the exact staged output across a failed inventory handoff.</summary>
public sealed class SWGlobalWarPhysicalProduction
{
    public TimeSpan ProductiveTime;
    public List<EntityUid>? Output;
}
