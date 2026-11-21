using Content.Shared.Interaction;
using Content.Shared.RCD.Systems;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._SW.GlobalWar;

[RegisterComponent, NetworkedComponent]
public sealed partial class SWFactoryBuilderComponent : Component;

[Prototype("swFactoryBuildRecipe")]
public sealed partial class SWFactoryBuildRecipePrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;
    [DataField(required: true)] public EntProtoId Entity;
    [DataField(required: true)] public Dictionary<string, int> Materials = new();
    [DataField] public int Radius;
}

[ByRefEvent]
public record struct SWFactoryBuildEvent(EntityUid User, EntityUid? Target, EntityCoordinates Location);

/// <summary>Keep the standard RCD preview and rotation, but replace operations only for the factory tool.</summary>
public sealed class SWFactoryBuilderInteractionSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWFactoryBuilderComponent, AfterInteractEvent>(OnUse, before: [typeof(RCDSystem)]);
    }

    private void OnUse(Entity<SWFactoryBuilderComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled)
            return;
        args.Handled = true;
        if (!args.CanReach)
            return;
        var request = new SWFactoryBuildEvent(args.User, args.Target, args.ClickLocation);
        RaiseLocalEvent(ent.Owner, ref request);
    }
}

[Serializable, NetSerializable]
public sealed class SWFactoryBuilderRefresh : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed record SWFactoryBuilderOption(string Id, string Name, string Entity,
    List<SWGlobalWarFactoryCost> Costs, long Price);

[Serializable, NetSerializable]
public sealed class SWFactoryBuilderSnapshot(string selected, List<SWFactoryBuilderOption> options,
    long budget, string status, bool allowed) : BoundUserInterfaceMessage
{
    public readonly string Selected = selected;
    public readonly List<SWFactoryBuilderOption> Options = options;
    public readonly long Budget = budget;
    public readonly string Status = status;
    public readonly bool Allowed = allowed;
}
