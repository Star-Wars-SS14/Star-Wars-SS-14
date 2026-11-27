using Content.Server.Radio.EntitySystems;
using Content.Shared.Labels.Components;
using Content.Shared.Telephone;
using Robust.Shared.Timing;

namespace Content.Server._SW.Holopads.ReportOnCall;

public sealed class SWHolopadCallReporterSystem : EntitySystem
{
    [Dependency] private readonly RadioSystem _radio = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        // TelephoneComponent exists on both call sides, so this directed handler fires
        // no matter which phone the attempt event is raised on.
        SubscribeLocalEvent<TelephoneComponent, TelephoneCallAttemptEvent>(OnCallAttemptDirected);
        // Fallback in case the attempt event is raised broadcast instead of directed.
        SubscribeLocalEvent<TelephoneCallAttemptEvent>(OnCallAttemptBroadcast);
    }

    private void OnCallAttemptDirected(Entity<TelephoneComponent> ent, ref TelephoneCallAttemptEvent ev)
    {
        Report(ev);
    }

    private void OnCallAttemptBroadcast(ref TelephoneCallAttemptEvent ev)
    {
        Report(ev);
    }

    private void Report(TelephoneCallAttemptEvent ev)
    {
        // The reporter lives on the receiving pad; its faction channel gets the alert.
        if (!TryComp(ev.Receiver, out SWHolopadCallReporterComponent? reporter))
            return;

        if (_timing.CurTime < reporter.NextMessageAvailable)
            return;

        // Report the calling holopad itself (its label), not the person behind it.
        var sourceUid = ev.Source.Owner;
        var padName = TryComp<LabelComponent>(sourceUid, out var label) && !string.IsNullOrWhiteSpace(label.CurrentLabel)
            ? label.CurrentLabel
            : Name(sourceUid);
        var message = Loc.GetString(reporter.MessageId, ("pad", padName));
        _radio.SendRadioMessage(ev.Receiver.Owner, message, reporter.ChannelId, ev.Receiver.Owner, false);
        reporter.NextMessageAvailable = _timing.CurTime + reporter.Delay;
    }
}