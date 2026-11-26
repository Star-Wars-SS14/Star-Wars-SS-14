using Content.Server._SW._Senate.Core;
using Content.Shared._SW._Senate;
using Robust.Server.GameObjects;

namespace Content.Server._SW._Senate.Consoles;

public sealed class SWCourtInfoConsoleSystem : EntitySystem
{
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly SWSenateCourtSystem _court = default!;

    private const float UpdateInterval = 3f;
    private float _accumulator;

    public override void Initialize()
    {
        SubscribeLocalEvent<SWCourtInfoConsoleComponent, BoundUIOpenedEvent>(OnOpened);
    }

    private void OnOpened(EntityUid uid, SWCourtInfoConsoleComponent comp, BoundUIOpenedEvent args)
    {
        UpdateUi(uid);
    }

    public override void Update(float frameTime)
    {
        if (!_time.ModeActive)
            return;

        _accumulator += frameTime;
        if (_accumulator < UpdateInterval)
            return;

        _accumulator = 0f;

        var query = EntityQueryEnumerator<SWCourtInfoConsoleComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            if (_ui.IsUiOpen(uid, SWCourtInfoConsoleUiKey.Key))
                UpdateUi(uid);
        }
    }

    private void UpdateUi(EntityUid uid)
    {
        var state = new SWCourtInfoConsoleState
        {
            GameTime = _time.TimeString(),
            BenchLabel = _court.BenchLabel(),
            Judges = _court.BuildJudges(),
            ProsecutorName = _court.Prosecutor ?? string.Empty,
            DefenderName = _court.Defender ?? string.Empty,
            Cases = _court.BuildRegistry(),
        };

        _ui.SetUiState(uid, SWCourtInfoConsoleUiKey.Key, state);
    }
}
