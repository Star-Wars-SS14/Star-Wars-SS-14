using Content.Shared.Echo.GameTicking.Components;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations;

namespace Content.Shared.Echo.CaptureConsole;

[NetSerializable, Serializable]
public enum CaptureConsoleUi : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class CaptureConsoleUpdateState(
    bool capturing,
    TimeSpan? startTime,
    TimeSpan timeRemain,
    TimeSpan captureDuration,
    SectorFractions fraction,
    SectorFractions? userFraction,
    string stationName,
    bool hasAccess)
    : BoundUserInterfaceState
{
    public bool Capturing = capturing;
    public TimeSpan? StartTime = startTime;
    public TimeSpan TimeRemain = timeRemain;
    public TimeSpan CaptureDuration = captureDuration;
    public SectorFractions StationFraction = fraction;
    public SectorFractions? UserFraction = userFraction;
    public string StationName = stationName;
    public bool HasAccess = hasAccess;
}

[RegisterComponent]
public sealed partial class AllianceStaffComponent  : Component;

[RegisterComponent]
public sealed partial class ResistanceStaffComponent  : Component;

[RegisterComponent]
public sealed partial class CaptureConsoleComponent : Component
{
    [DataField(customTypeSerializer: typeof(TimespanSerializer))]
    public TimeSpan StartTime;

    [DataField(customTypeSerializer: typeof(TimespanSerializer))]
    public TimeSpan CaptureDuration = TimeSpan.FromMinutes(15);

    [ViewVariables(VVAccess.ReadWrite)]
    public bool Capturing = false;

    [DataField]
    public SectorFractions CapturingFraction = SectorFractions.Alliance;
}

[Serializable, NetSerializable]
public sealed class CaptureConsoleButtonPressedMessage : BoundUserInterfaceMessage;
