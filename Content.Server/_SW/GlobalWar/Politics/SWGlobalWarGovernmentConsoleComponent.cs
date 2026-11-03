using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;

namespace Content.Server._SW.GlobalWar.Politics;

/// <summary>Physical government terminal; its faction limits access but never grants an office.</summary>
[RegisterComponent]
public sealed partial class SWGlobalWarGovernmentConsoleComponent : Component
{
    /// <summary>Government whose terminal this is.</summary>
    [DataField(required: true)]
    public SWGlobalWarFaction Faction;

    /// <summary>Government service presented by this terminal.</summary>
    [DataField(required: true)]
    public SWGlobalWarGovernmentConsoleKind Kind;
}
