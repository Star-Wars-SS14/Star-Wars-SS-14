using Content.Shared.Actions;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._SW.SaberForms;

[Serializable, NetSerializable]
public enum SWSaberFormsUiKey
{
    Key,
}

[Serializable, NetSerializable]
public sealed class SWSaberFormsUiState(
    NetEntity weapon,
    ProtoId<SWSaberFormPrototype> activeForm,
    List<ProtoId<SWSaberFormPrototype>> allowedForms) : BoundUserInterfaceState
{
    public readonly NetEntity Weapon = weapon;
    public readonly ProtoId<SWSaberFormPrototype> ActiveForm = activeForm;
    public readonly List<ProtoId<SWSaberFormPrototype>> AllowedForms = allowedForms;
}

[Serializable, NetSerializable]
public sealed class SWSaberFormSelectMessage(
    ProtoId<SWSaberFormPrototype> form,
    NetEntity weapon) : BoundUserInterfaceMessage
{
    public readonly ProtoId<SWSaberFormPrototype> Form = form;
    public readonly NetEntity Weapon = weapon;
}

public sealed partial class SWSaberFormsActionEvent : InstantActionEvent;

[ByRefEvent]
public readonly record struct SWSaberFormDefenseEvent(EntityCoordinates Coordinates, bool PlayClash);
