using Robust.Shared.GameStates;

namespace Content.Shared._SW.Droids;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SWDroidCommandableComponent : Component
{
    [DataField(required: true), AutoNetworkedField]
    public string Faction = string.Empty;

    [DataField, AutoNetworkedField]
    public EntityUid? ActiveCommander;

    [DataField, AutoNetworkedField]
    public int ActiveCommanderRank;

    [DataField]
    public float CommandRange = 3f;
}
