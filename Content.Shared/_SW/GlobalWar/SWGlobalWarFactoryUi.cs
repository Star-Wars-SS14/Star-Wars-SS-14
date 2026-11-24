using Robust.Shared.Serialization;

namespace Content.Shared._SW.GlobalWar;

[Serializable, NetSerializable]
public enum SWGlobalWarFactoryUiKey : byte { Key }

[Serializable, NetSerializable]
public enum SWGlobalWarFactoryAction : byte
{
    Start, Stop, Insert, Take, Recruit, Materialize, Produce, PreviousPage, NextPage, CargoWork,
}

[Serializable, NetSerializable]
public sealed class SWGlobalWarFactoryOpen(Guid subscription) : BoundUserInterfaceMessage
{
    public readonly Guid Subscription = subscription;
}

[Serializable, NetSerializable]
public sealed class SWGlobalWarFactorySnapshot(SWGlobalWarFactoryState state, Guid subscription, NetEntity viewer)
    : BoundUserInterfaceMessage
{
    public readonly SWGlobalWarFactoryState State = state;
    public readonly Guid Subscription = subscription;
    public readonly NetEntity Viewer = viewer;
}

[Serializable, NetSerializable]
public sealed record SWGlobalWarFactoryItem(NetEntity Entity, string Name, bool CanTake, string? Prototype = null, int Count = 1);

[Serializable, NetSerializable]
public sealed record SWGlobalWarFactoryCost(string Name, string Entity, int Need, int Have);

[Serializable, NetSerializable]
public sealed record SWGlobalWarFactoryProduct(SWGlobalWarSupplyKind? Kind, string? Recipe, string Name, string? Entity,
    int Output, int Seconds, List<SWGlobalWarFactoryCost> Costs, bool Ready);

[Serializable, NetSerializable]
public sealed record SWGlobalWarFactoryChoice(string Id, string Name);

/// <summary>The viewer's delivery task at this very point: the market console that assigns it stands elsewhere.</summary>
[Serializable, NetSerializable]
public sealed record SWGlobalWarFactoryCargoWork(Guid Work, bool Accept, string Item, int Count, NetEntity? Carrier, bool InReach);

[Serializable, NetSerializable]
public sealed class SWGlobalWarFactoryState(Guid generation, long revision, bool operate, bool factory, bool recruitment,
    SWGlobalWarSupplyKind kind, string status, List<SWGlobalWarFactoryItem> items,
    List<SWGlobalWarFactoryChoice> materials, List<SWGlobalWarFactoryChoice> recipes,
    int page, int pages, SWGlobalWarSupplyKind[] kinds, bool mining, bool arsenal = false,
    bool powered = false, bool enabled = false, int remainingSeconds = 0, int totalSeconds = 0, string detail = "",
    List<SWGlobalWarFactoryProduct>? products = null, SWGlobalWarFactoryCargoWork? cargoWork = null) : BoundUserInterfaceState
{
    public readonly Guid Generation = generation;
    public readonly long Revision = revision;
    public readonly bool Operate = operate;
    public readonly bool Factory = factory;
    public readonly bool Recruitment = recruitment;
    public readonly SWGlobalWarSupplyKind Kind = kind;
    public readonly string Status = status;
    public readonly List<SWGlobalWarFactoryItem> Items = items;
    public readonly List<SWGlobalWarFactoryChoice> Materials = materials;
    public readonly List<SWGlobalWarFactoryChoice> Recipes = recipes;
    public readonly int Page = page;
    public readonly int Pages = pages;
    public readonly SWGlobalWarSupplyKind[] Kinds = kinds;
    public readonly bool Mining = mining;
    public readonly bool Arsenal = arsenal;
    public readonly bool Powered = powered;
    public readonly bool Enabled = enabled;
    public readonly int RemainingSeconds = remainingSeconds;
    public readonly int TotalSeconds = totalSeconds;
    public readonly string Detail = detail;
    public readonly List<SWGlobalWarFactoryProduct> Products = products ?? new();
    public readonly SWGlobalWarFactoryCargoWork? CargoWork = cargoWork;
}

[Serializable, NetSerializable]
public sealed class SWGlobalWarFactoryRequest(Guid generation, Guid request, long revision,
    SWGlobalWarFactoryAction action, SWGlobalWarSupplyKind kind, NetEntity? item = null,
    string selection = "") : BoundUserInterfaceMessage
{
    public readonly Guid Generation = generation;
    public readonly Guid Request = request;
    public readonly long Revision = revision;
    public readonly SWGlobalWarFactoryAction Action = action;
    public readonly SWGlobalWarSupplyKind Kind = kind;
    public readonly NetEntity? Item = item;
    public readonly string Selection = selection;
}
