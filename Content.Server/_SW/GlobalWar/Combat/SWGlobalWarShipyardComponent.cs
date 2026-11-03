using Content.Shared._SW.GlobalWar.Combat;

namespace Content.Server._SW.GlobalWar.Combat;

/// <summary>Physical order terminal. It resolves its explicitly linked dock and clearance before delivery.</summary>
[RegisterComponent, Access(typeof(SWGlobalWarFleetSystem))]
public sealed partial class SWGlobalWarShipyardComponent : Component
{
    /// <summary>Largest catalogue tier accepted at this location.</summary>
    [DataField]
    public SWGlobalWarShipyardTier Tier = SWGlobalWarShipyardTier.SmallMedium;
}

/// <summary>Campaign hull class used for fleet limits, including captured ships.</summary>
[RegisterComponent, Access(typeof(SWGlobalWarFleetSystem))]
public sealed partial class SWGlobalWarFleetShipComponent : Component
{
    /// <summary>Server-selected hull class; the client never chooses it after delivery.</summary>
    [DataField]
    public SWGlobalWarShipClass Class;
}

/// <summary>Enables the finite starting grant only for the full campaign, not ordinary legacy game rules.</summary>
[RegisterComponent]
public sealed partial class SWGlobalWarStartingFleetComponent : Component;
