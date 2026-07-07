namespace Content.Server._SW._Senate.Antag;

[RegisterComponent]
public sealed partial class SWMercMemberComponent : Component;

[RegisterComponent]
public sealed partial class SWMercSpawnComponent : Component;

[RegisterComponent]
public sealed partial class SWMercEvacPointComponent : Component
{
    [DataField]
    public float Range = 3f;
}

[RegisterComponent]
public sealed partial class SWShadowTerminalComponent : Component;
