using Content.Shared._SW.GlobalWar;

namespace Content.Server._SW.GlobalWar.Identity;

/// <summary>Server identity on a mind. Body labels and equipment do not grant these rights.</summary>
[RegisterComponent, Access(typeof(SWGlobalWarIdentitySystem))]
public sealed partial class SWGlobalWarIdentityComponent : Component
{
    public Guid Generation;
    public SWGlobalWarFaction Faction;

    /// <summary>
    /// The body this mind was enrolled with. A body the mind enters later by other means, such as an admin's
    /// controlmob, carries no side. Null until the enrolled mind first owns a body.
    /// </summary>
    public EntityUid? Body;
}
