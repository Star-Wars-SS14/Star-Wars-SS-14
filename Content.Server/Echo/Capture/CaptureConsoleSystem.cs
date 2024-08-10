using System.Linq;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Server.Xenoarchaeology.Equipment.Components;
using Content.Server.Xenoarchaeology.XenoArtifacts;
using Content.Shared.Audio;
using Content.Shared.Echo.CaptureConsole;
using Content.Shared.Echo.GameTicking.Components;
using Content.Shared.UserInterface;
using Content.Shared.Xenoarchaeology.Equipment;
using JetBrains.Annotations;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server.Echo.Capture;

public sealed class CaptureConsoleSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedAmbientSoundSystem _ambientSound = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly ArtifactSystem _artifact = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        SubscribeLocalEvent<CaptureConsoleComponent, CaptureConsoleButtonPressedMessage>(OnCaptureButton);

        SubscribeLocalEvent<CaptureConsoleComponent, BeforeActivatableUIOpenEvent>(UpdateUserInterface);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<CaptureConsoleComponent>();
        while (query.MoveNext(out var uid, out var console))
        {
            if (_timing.CurTime - console.StartTime < console.CaptureDuration - console.AccumulatedRunTime)
                continue;

            FinishCapturing(uid, console);
        }
    }

    private void FinishCapturing(EntityUid uid, CaptureConsoleComponent console)
    {
        //TODO
    }

    private void UpdateUserInterface(EntityUid uid,
        CaptureConsoleComponent? component = null,
        BeforeActivatableUIOpenEvent? args = null)
    {
        if (!Resolve(uid, ref component, false))
            return;

        var currentFraction = SectorFractions.Alliance;

        if (TryComp<CapturableStationComponent>(_station.GetOwningStation(uid), out var capturableStationComponent))
            currentFraction = capturableStationComponent.Fraction;

        TimeSpan? totalTime = null;
        SectorFractions? userFraction;
        if (HasComp<AllianceStaffComponent>(uid))
        {
            userFraction = SectorFractions.Alliance;
        }
        else if (HasComp<ResistanceStaffComponent>(uid))
        {
            userFraction = SectorFractions.Resistance;
        }
        else
        {
            userFraction = null;
        }

        var station = GetNetEntity(_station.GetOwningStation(uid));
        var name = _station.GetStationNames().First(x => x.Entity == station).Name;
        var state = new CaptureConsoleUpdateState(
            component.Capturing,
            component.StartTime,
            component.AccumulatedRunTime,
            totalTime ?? TimeSpan.Zero,
            currentFraction,
            userFraction,
            name
        );

        _ui.SetUiState(uid, CaptureConsoleUi.Key, state);
    }

    private void OnCaptureButton(EntityUid uid,
        CaptureConsoleComponent component,
        CaptureConsoleButtonPressedMessage args)
    {
        //TODO

        UpdateUserInterface(uid, component);
    }

    /// <summary>
    /// Stops the current scan
    /// </summary>
    [PublicAPI]
    public void CancelScan(EntityUid artifact,
        ActiveScannedArtifactComponent? component = null,
        ArtifactAnalyzerComponent? analyzer = null)
    {
        if (!Resolve(artifact, ref component, false))
            return;

        if (!Resolve(component.Scanner, ref analyzer))
            return;

        _audio.PlayPvs(component.ScanFailureSound, component.Scanner, AudioParams.Default.WithVolume(3f));

        RemComp<ActiveArtifactAnalyzerComponent>(component.Scanner);
        if (analyzer.Console != null)
            UpdateUserInterface(analyzer.Console.Value);

        RemCompDeferred(artifact, component);
    }
}
