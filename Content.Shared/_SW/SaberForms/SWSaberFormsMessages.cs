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

[Serializable, NetSerializable]
public enum SWSaberDefenseVisual : byte
{
    None,
    SparkFan,
    RiposteGlint,
    ReflectionCurl,
}

[ByRefEvent]
public readonly record struct SWSaberFormDefenseEvent(
    EntityCoordinates Coordinates,
    SWSaberDefenseVisual Visual,
    Angle Direction,
    Angle SourceDirection,
    bool PlayClash);

/// <summary>One confirmed defense effect, anchored with its directions in the contact's coordinate frame.</summary>
[Serializable, NetSerializable]
public sealed class SWSaberDefenseEffectEvent(
    NetCoordinates coordinates,
    SWSaberDefenseVisual visual,
    Angle direction,
    Angle sourceDirection) : EntityEventArgs
{
    public readonly NetCoordinates Coordinates = coordinates;
    public readonly SWSaberDefenseVisual Visual = visual;
    public readonly Angle Direction = direction;
    public readonly Angle SourceDirection = sourceDirection;
}
