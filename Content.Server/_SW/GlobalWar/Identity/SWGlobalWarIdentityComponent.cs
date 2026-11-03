using Content.Shared._SW.GlobalWar;

namespace Content.Server._SW.GlobalWar.Identity;

/// <summary>Server identity on a mind. Body labels and equipment do not grant these rights.</summary>
[RegisterComponent, Access(typeof(SWGlobalWarIdentitySystem))]
public sealed partial class SWGlobalWarIdentityComponent : Component
{
    public Guid Generation;
    public SWGlobalWarFaction Faction;
}
