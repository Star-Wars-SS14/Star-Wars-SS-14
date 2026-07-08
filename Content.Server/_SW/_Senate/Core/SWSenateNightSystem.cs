using Content.Shared.GameTicking;
using Robust.Shared.Map.Components;

namespace Content.Server._SW._Senate.Core;

public sealed class SWSenateNightSystem : EntitySystem
{
    [Dependency] private readonly SWSenateTimeSystem _time = default!;

    private static readonly Color NightColor = Color.FromHex("#30304AFF");
    private const float FadeSeconds = 8f;
    private const float TickInterval = 0.5f;

    private readonly Dictionary<EntityUid, Color> _dayColors = new();
    private float _accum;
    private float _fade;

    public override void Initialize()
    {
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _dayColors.Clear();
        _accum = 0f;
        _fade = 0f;
    }

    public override void Update(float frameTime)
    {
        if (!_time.ModeActive)
            return;

        _accum += frameTime;
        if (_accum < TickInterval)
            return;

        var dt = _accum;
        _accum = 0f;

        var goal = _time.DayActive ? 0f : 1f;
        if (_fade == goal)
            return;

        _fade = goal > _fade
            ? Math.Min(_fade + dt / FadeSeconds, 1f)
            : Math.Max(_fade - dt / FadeSeconds, 0f);

        var query = EntityQueryEnumerator<MapLightComponent>();
        while (query.MoveNext(out var uid, out var light))
        {
            if (!_dayColors.TryGetValue(uid, out var day))
            {
                day = light.AmbientLightColor;
                _dayColors[uid] = day;
            }

            light.AmbientLightColor = Color.InterpolateBetween(day, NightColor, _fade);
            Dirty(uid, light);
        }
    }
}
