using Content.Shared.Inventory;
using Content.Shared.Roles;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._SW.Overlays;

/// <summary>
/// Identifies allied ID cards while this item is worn in a permitted slot.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SWFactionJobHudComponent : Component
{
    [DataField(required: true), AutoNetworkedField]
    public string Faction = string.Empty;

    [DataField, AutoNetworkedField]
    public SlotFlags SlotFlags = SlotFlags.HEAD;

    /// <summary>
    /// All departments on the target's ID must belong to this faction.
    /// Empty and mixed-faction cards are not identified.
    /// </summary>
    [DataField(required: true), AutoNetworkedField]
    public HashSet<ProtoId<DepartmentPrototype>> Departments = new();
}
