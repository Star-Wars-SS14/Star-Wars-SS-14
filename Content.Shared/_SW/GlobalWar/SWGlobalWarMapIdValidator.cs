using Content.Shared.Maps;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Markdown.Validation;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Generic;
using Robust.Shared.Serialization.TypeSerializers.Interfaces;

namespace Content.Shared._SW.GlobalWar;

/// <summary>Validates server map bindings without asking a client to resolve its intentionally ignored map kind.</summary>
public sealed class SWGlobalWarMapIdValidator : ITypeValidator<ProtoId<GameMapPrototype>, ValueDataNode>
{
    public ValidationNode Validate(ISerializationManager serializationManager, ValueDataNode node,
        IDependencyCollection dependencies, ISerializationContext? context = null)
    {
        var prototypes = dependencies.Resolve<IPrototypeManager>();
        if (dependencies.Resolve<INetManager>().IsClient && prototypes.IsIgnored("gameMap"))
            return new InconclusiveNode(node);

        return ProtoIdSerializer<GameMapPrototype>.Validate(dependencies, node);
    }
}
