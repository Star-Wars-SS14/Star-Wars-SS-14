using Content.Shared.Echo.GameTicking.Components;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations;
using Robust.Shared.Utility;

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
    TimeSpan? accumulatedRunTime,
    TimeSpan totalTime,
    SectorFractions fraction,
    SectorFractions? userFraction,
    string stationName
)
    : BoundUserInterfaceState
{
    public bool Capturing = capturing;
    public TimeSpan? StartTime = startTime;
    public TimeSpan? AccumulatedRunTime = accumulatedRunTime;
    public TimeSpan TotalTime = totalTime;
    public SectorFractions StationFraction = fraction;
    public SectorFractions? UserFraction = userFraction;
    public string StationName = stationName;
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
    public TimeSpan CaptureDuration = TimeSpan.FromMinutes(30);

    [ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan AccumulatedRunTime;

    [ViewVariables(VVAccess.ReadWrite)]
    public bool Capturing = false;

    [DataField]
    public EntityUid Artifact;
}

[Serializable, NetSerializable]
public sealed class CaptureConsoleButtonPressedMessage : BoundUserInterfaceMessage;
