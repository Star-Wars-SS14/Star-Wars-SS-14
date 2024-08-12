using System.Linq;
using Content.Server.Chat.Systems;
using Content.Server.Echo.GameTicking.Rules.Components;
using Content.Server.Station.Systems;
using Content.Server.Xenoarchaeology.XenoArtifacts;
using Content.Shared.Audio;
using Content.Shared.Echo.CaptureConsole;
using Content.Shared.Echo.GameTicking.Components;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server.Echo.Capture;

public sealed class CaptureConsoleSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly ChatSystem _chat = default!;

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
            if (!console.IsTimeout)
            {
                goto y;
            }

            if (_timing.CurTime - console.TimeoutStart > TimeSpan.FromSeconds(15))
            {
                console.IsTimeout = false;
                UpdateUserInterface(uid);
            }

            y:
            if (!console.Capturing)
                continue;

            if (_timing.CurTime - console.StartTime < console.CaptureDuration)
                continue;

            FinishCapturing(uid, ref console);
        }
    }

    private void FinishCapturing(EntityUid uid, ref CaptureConsoleComponent console)
    {
        var station = _station.GetOwningStation(uid);
        var name = _station.GetStationNames().FirstOrNull(x => x.Entity == GetNetEntity(station))?.Name;

        console.Capturing = false;

        if (station == null)
            return;

        EnsureComp<CapturableStationComponent>(station.Value, out var capturable);

        capturable.Fraction = console.CapturingFraction;

        var fractionText = capturable.Fraction switch
        {
            SectorFractions.Alliance => Loc.GetString("capture-console-message-fraction-alliance"),
            SectorFractions.Resistance => Loc.GetString("capture-console-message-fraction-resistance"),
            _ => Loc.GetString("capture-console-message-user-no-fraction"),
        };


        _chat.DispatchGlobalAnnouncement(Loc.GetString("station-capture-finish-announcement",
                ("name", name ?? "Unknown"),
                ("fraction", fractionText)),
            playSound: false,
            colorOverride: Color.Yellow);

        var query = EntityQueryEnumerator<SectorWarRuleComponent>();
        while (query.MoveNext(out var rule))
        {
            switch (capturable.Fraction)
            {
                case SectorFractions.Alliance:
                    rule.AllianceCredits += rule.CaptureReward;
                    break;
                case SectorFractions.Resistance:
                    rule.ResistanceCredits += rule.CaptureReward;
                    break;
            }
        }

        UpdateUserInterface(uid);
    }

    private void UpdateUserInterface(EntityUid uid,
        CaptureConsoleComponent? component = null,
        BeforeActivatableUIOpenEvent? args = null)
    {
        if (!Resolve(uid, ref component, false))
            return;

        var currentFraction = SectorFractions.Alliance;
        var hasAccess = true;
        if (TryComp<CapturableStationComponent>(_station.GetOwningStation(uid), out var capturableStationComponent))
            currentFraction = capturableStationComponent.Fraction;

        TimeSpan? totalTime = component.CaptureDuration - (_timing.CurTime - component.StartTime);

        SectorFractions? userFraction = null;
        if (args != null)
        {
            if (HasComp<AllianceStaffComponent>(args.User))
            {
                userFraction = SectorFractions.Alliance;
            }
            else if (HasComp<ResistanceStaffComponent>(args.User))
            {
                userFraction = SectorFractions.Resistance;
            }
            else
            {
                hasAccess = false;
            }
        }


        var station = GetNetEntity(_station.GetOwningStation(uid));
        var name = _station.GetStationNames().FirstOrNull(x => x.Entity == station)?.Name;
        var state = new CaptureConsoleUpdateState(
            component.Capturing,
            component.StartTime,
            component.Capturing ? totalTime ?? TimeSpan.Zero : TimeSpan.Zero,
            component.CaptureDuration,
            currentFraction,
            userFraction,
            name ?? "Unknown",
            hasAccess,
            component.IsTimeout
        );

        _ui.SetUiState(uid, CaptureConsoleUi.Key, state);
    }

    private void OnCaptureButton(EntityUid uid,
        CaptureConsoleComponent component,
        CaptureConsoleButtonPressedMessage args)
    {
        component.TimeoutStart = _timing.CurTime;
        component.IsTimeout = true;

        var station = GetNetEntity(_station.GetOwningStation(uid));
        var name = _station.GetStationNames().FirstOrNull(x => x.Entity == station)?.Name;

        SectorFractions? fraction;

        if (HasComp<AllianceStaffComponent>(args.Actor))
        {
            fraction = SectorFractions.Alliance;
        }
        else if (HasComp<ResistanceStaffComponent>(args.Actor))
        {
            fraction = SectorFractions.Resistance;
        }
        else
        {
            return;
        }

        if (component.Capturing)
        {
            component.Capturing = false;
            _chat.DispatchGlobalAnnouncement(
                Loc.GetString("station-capture-cancel-announcement", ("name", name ?? "Unknown")),
                null,
                false,
                colorOverride: Color.Yellow);
            goto x;
        }

        component.Capturing = true;
        component.CapturingFraction = fraction.Value;

        component.StartTime = _timing.CurTime;
        _chat.DispatchGlobalAnnouncement(
            Loc.GetString("station-capture-start-announcement", ("name", name ?? "Unknown")),
            null,
            false,
            colorOverride: Color.Yellow);
        var query = EntityQueryEnumerator<SectorWarRuleComponent>();
        while (query.MoveNext(out var rule))
        {
            var captureSound = rule.CaptureSound;

            if (rule.CurrentAudio == null || Deleted(rule.CurrentAudio.Value.Entity))
            {
                rule.CurrentAudio = _audio.PlayGlobal(captureSound, Filter.Broadcast(), true);
            }
        }

        x:

        UpdateUserInterface(uid);
    }
}
