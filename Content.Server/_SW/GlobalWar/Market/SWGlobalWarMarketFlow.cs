using Content.Shared._SW.GlobalWar;

namespace Content.Server._SW.GlobalWar.Market;

/// <summary>One campaign's editable contract, with a per-flow revision independent of the economic clock.</summary>
public sealed class SWGlobalWarMarketFlow(SWGlobalWarEconomyFlowPrototype definition)
{
    internal SWGlobalWarMarketFlow(string id, string source, string destination, string commodity,
        SWGlobalWarFlowPriority priority) : this(new SWGlobalWarEconomyFlowPrototype
        {
            Source = source, Destination = destination, Commodity = commodity, Priority = priority,
            RateMode = SWGlobalWarFlowRateMode.Fixed,
        })
    {
        Id = id;
    }

    public string Id { get; } = definition.ID;
    public SWGlobalWarFaction ContractFaction { get; internal set; }
    public SWGlobalWarFaction ExpectedRecipient { get; internal set; }
    public string Source { get; internal set; } = definition.Source.Id;
    public string Destination { get; internal set; } = definition.Destination.Id;
    public string Commodity { get; internal set; } = definition.Commodity.Id;
    public SWGlobalWarFlowPriority Priority { get; internal set; } = definition.Priority;
    public SWGlobalWarFlowRateMode RateMode { get; } = definition.RateMode;
    public decimal Share { get; } = definition.Share;
    public long Revision { get; internal set; }
    public int Order { get; internal set; }
    public bool Enabled { get; internal set; } = true;
    public decimal? ExplicitRate { get; internal set; }
    public decimal PerMinute { get; internal set; }
    private readonly Dictionary<string, SWGlobalWarFlowStatistics> _statistics = new();
    public IReadOnlyDictionary<string, SWGlobalWarFlowStatistics> Statistics => _statistics;
    public long Dispatched { get => GetStatistics(Commodity).Dispatched; internal set => GetStatistics(Commodity).Dispatched = value; }
    public long Received { get => GetStatistics(Commodity).Received; internal set => GetStatistics(Commodity).Received = value; }
    public long Returned { get => GetStatistics(Commodity).Returned; internal set => GetStatistics(Commodity).Returned = value; }
    public long ReturnCaptured { get => GetStatistics(Commodity).ReturnCaptured; internal set => GetStatistics(Commodity).ReturnCaptured = value; }
    public long ReturnRedirected { get => GetStatistics(Commodity).ReturnRedirected; internal set => GetStatistics(Commodity).ReturnRedirected = value; }
    public long RedirectedReturnReceived { get => GetStatistics(Commodity).RedirectedReturnReceived; internal set => GetStatistics(Commodity).RedirectedReturnReceived = value; }
    public string WaitReason { get; internal set; } = string.Empty;
    internal decimal DispatchBudget;
    internal string[]? Route;

    /// <summary>Historical cargo retains its commodity after the contract changes commodity or destination.</summary>
    internal SWGlobalWarFlowStatistics GetStatistics(string commodity)
    {
        if (!_statistics.TryGetValue(commodity, out var statistics))
            _statistics.Add(commodity, statistics = new());
        return statistics;
    }
}

public sealed class SWGlobalWarFlowStatistics
{
    public long Dispatched { get; internal set; }
    public long Received { get; internal set; }
    public long Returned { get; internal set; }
    public long ReturnCaptured { get; internal set; }
    public long ReturnRedirected { get; internal set; }
    public long RedirectedReturnReceived { get; internal set; }
}
