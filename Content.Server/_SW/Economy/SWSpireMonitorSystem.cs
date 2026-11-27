using Content.Shared._SW.Economy;
using Content.Shared._SW.TaskSystem;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._SW.Economy;

/// <summary>
/// Aggregates every SWFactionSpire into a read-only monitor state. Pushes on UI
/// open and periodically (1s) so capture/payout progress moves without any input.
/// Row selection pings back a standard console beep.
/// </summary>
public sealed class SWSpireMonitorSystem : EntitySystem
{
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IComponentFactory _componentFactory = default!;

    private const float PushInterval = 1.0f;
    private float _pushAccumulator;

    private static readonly SoundPathSpecifier BeepSound = new("/Audio/_SW/board/beep_board.ogg");

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWSpireMonitorComponent, BoundUIOpenedEvent>(OnOpened);
        SubscribeLocalEvent<SWSpireMonitorComponent, SWSpireMonitorSelectMessage>(OnSelectMessage);
    }

    private void OnOpened(Entity<SWSpireMonitorComponent> ent, ref BoundUIOpenedEvent args)
    {
        PushState(ent);
    }

    private void OnSelectMessage(Entity<SWSpireMonitorComponent> ent, ref SWSpireMonitorSelectMessage args)
    {
        _audio.PlayPvs(BeepSound, ent.Owner);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _pushAccumulator += frameTime;
        if (_pushAccumulator < PushInterval)
            return;
        _pushAccumulator = 0f;

        var query = EntityQueryEnumerator<SWSpireMonitorComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            PushState(new Entity<SWSpireMonitorComponent>(uid, comp));
        }
    }

    private void PushState(Entity<SWSpireMonitorComponent> ent)
    {
        var state = new SWSpireMonitorBoundUserInterfaceState();

        var spires = EntityQueryEnumerator<SWFactionSpireComponent>();
        while (spires.MoveNext(out var spireUid, out var spire))
        {
            state.Rows.Add(new SWSpireMonitorRowData
            {
                SpireName = MetaData(spireUid).EntityName ?? string.Empty,
                OwnerFaction = spire.OwnerFaction,
                OwnerName = spire.OwnerFaction == string.Empty
                    ? Loc.GetString("sw-spire-neutral")
                    : NameOf(spire.OwnerFaction),
                CapturingName = spire.CapturingFaction == string.Empty
                    ? string.Empty
                    : NameOf(spire.CapturingFaction),
                IsCapturing = spire.IsCapturing,
                CaptureProgress = spire.CaptureProgress,
                PayoutProgress = spire.PayoutProgress,
                CaptureRemainingSeconds = spire.IsCapturing
                    ? spire.CaptureTime * (1f - spire.CaptureProgress)
                    : 0f,
                PayoutRemainingSeconds = spire.OwnerFaction != string.Empty
                    ? spire.PayoutInterval * (1f - spire.PayoutProgress)
                    : 0f,
            });
        }

        _ui.SetUiState(ent.Owner, SWSpireMonitorUiKey.Key, state);
    }

    private string NameOf(string factionId)
    {
        if (_prototype.TryIndex<EntityPrototype>(factionId, out var proto) &&
            proto.TryGetComponent<SWFactionComponent>(out var faction, _componentFactory))
        {
            return faction.Name;
        }
        return factionId;
    }
}