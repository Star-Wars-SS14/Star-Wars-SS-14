using System.Linq;
using Content.Server._SW.TaskSystem;
using Content.Server.Chat.Systems;
using Content.Server.Echo.GameTicking.Rules.Components;
using Content.Server.Station.Systems;
using Content.Server.Xenoarchaeology.XenoArtifacts;
using Content.Shared._SW.TaskSystem;
using Content.Shared.Access.Systems;
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
using Robust.Shared.Map;
using Microsoft.CodeAnalysis.Elfie.Model;
namespace Content.Server.Echo.Capture;

public sealed class CaptureConsoleSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly AccessReaderSystem _accessReader = default!;
    [Dependency] private readonly SWFactionGameruleSystem _sWFaction = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

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

        
        var fractionText = "";
        var faction = _sWFaction.GetFactionEntity(console.CapturingFraction);

        if (faction != null)
        {
            fractionText = faction.Value.Comp.Name;
        }

        _chat.DispatchGlobalAnnouncement(Loc.GetString("station-capture-finish-announcement",
                ("name", name ?? "Unknown"),
                ("fraction", fractionText)),
            playSound: false,
            colorOverride: Color.Yellow);
        var query = EntityQueryEnumerator<SectorWarRuleComponent>();
        while (query.MoveNext(out var rule))
        {
            if (capturable.Fraction != null) {
                rule.FactionsScore[capturable.Fraction]--;
            }
            rule.FactionsScore[console.CapturingFraction]++;
            capturable.Fraction = console.CapturingFraction;
            rule.FactionMoney[console.CapturingFraction] += capturable.CaptureReward;
        }

        UpdateUserInterface(uid);
    }

    private void UpdateUserInterface(EntityUid uid,
        CaptureConsoleComponent? component = null,
        BeforeActivatableUIOpenEvent? args = null)
    {
        if (!Resolve(uid, ref component, false))
            return;

        var currentFraction = "";
        var hasAccess = true;
        var station = _station.GetOwningStation(uid);

        if (station != null
            && TryComp<CapturableStationComponent>(station, out var capturableStationComponent))
            currentFraction = capturableStationComponent.Fraction;

        TimeSpan? totalTime = component.CaptureDuration - (_timing.CurTime - component.StartTime);

        string? userFraction = null;
        if (args != null)
        {
            if (TryComp<SWFactionMemberComponent>(args.User, out var comp))
            {
                userFraction = comp.Faction;
            }
        }


        var stationNet = GetNetEntity(_station.GetOwningStation(uid));
        var name = _station.GetStationNames().FirstOrNull(x => x.Entity == stationNet)?.Name;
        ISawmill _s = Logger.GetSawmill("sdfg");
        _s.Debug($"cf : {currentFraction}");
        var tmp_faction = currentFraction != null ? _sWFaction.GetFactionEntity(currentFraction) : null;
        string stationFactionName = tmp_faction != null ? tmp_faction.Value.Comp.Name : "Нет фракции";

        tmp_faction = userFraction != null ? _sWFaction.GetFactionEntity(userFraction) : null;
        string userFactionName = tmp_faction != null ? tmp_faction.Value.Comp.Name : "uknown";

        var state = new CaptureConsoleUpdateState(
            component.Capturing,
            component.StartTime,
            component.Capturing ? totalTime ?? TimeSpan.Zero : TimeSpan.Zero,
            component.CaptureDuration,
            stationFactionName,
            userFactionName,
            name ?? "Unknown",
            hasAccess,
            component.IsTimeout);

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

        if (!TryComp<SWFactionMemberComponent>(args.Actor, out var factionComp))
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
        }
        else
        {
            component.Capturing = true;
            component.CapturingFraction = factionComp.Faction;

            component.StartTime = _timing.CurTime;
            _chat.DispatchGlobalAnnouncement(
                Loc.GetString("station-capture-start-announcement", ("name", name ?? "Unknown")),
                null,
                false,
                colorOverride: Color.Yellow);
            var query = EntityQueryEnumerator<SectorWarRuleComponent>();
            while (query.MoveNext(out var rule))
            {
                if (rule.CaptureSound == null) {
                    continue;
                }
                var captureSound = rule.CaptureSound;

                if (rule.CurrentAudio == null || Deleted(rule.CurrentAudio.Value.Entity))
                {
                    rule.CurrentAudio = _audio.PlayGlobal(captureSound, Filter.Broadcast(), true);
                }
            }
        }

        UpdateUserInterface(uid);
    }
}
