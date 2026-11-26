using System.Linq;
using Content.Shared.GameTicking;

namespace Content.Server._SW._Senate.Core;

/// <summary>Short-lived confidence in a successful ministry intervention, measured in active game time.</summary>
public sealed class SWSenateTransientSituationSystem : EntitySystem
{
    [Dependency] private readonly SWSenateStateSystem _senate = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    private const double Lifetime = 6;
    private double _lastPublished = -1;
    private readonly Dictionary<(string Source, string Sector, string Stat), (float Strength, double Start)> _effects = new();

    public override void Initialize()
    {
        UpdatesBefore.Add(typeof(SWSenateTimeSystem));
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => { _effects.Clear(); _lastPublished = -1; });
    }

    public void Refresh(string source, string sector, string substat, float strength)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(sector) || !float.IsFinite(strength) || strength <= 0) return;
        var key = (source, sector, substat);
        var now = _time.ActiveGameHours;
        var prior = _effects.TryGetValue(key, out var old) ? old.Strength * (float) Math.Clamp(1 - (now - old.Start) / Lifetime, 0, 1) : 0;
        _effects[key] = (Math.Clamp(Math.Max(strength, prior), 0, 5), now);
        Publish(source, now);
    }

    public override void Update(float frameTime)
    {
        if (!_time.ModeActive) return;
        var now = _time.ActiveGameHours;
        if (now - _lastPublished < .05) return;
        _lastPublished = now;
        foreach (var source in _effects.Keys.Select(k => k.Source).Distinct().ToArray())
            Publish(source, now);
    }

    private void Publish(string source, double now)
    {
        var keys = _effects.Keys.Where(k => k.Source == source).ToArray();
        var result = new List<SWSituationalContribution>();
        foreach (var key in keys)
        {
            var effect = _effects[key];
            var factor = Math.Clamp(1 - (now - effect.Start) / Lifetime, 0, 1);
            if (factor <= 0) { _effects.Remove(key); continue; }
            result.Add(new SWSituationalContribution(key.Sector, key.Stat, effect.Strength * (float) factor));
        }
        _senate.ReplaceSituationalContributions(source, result);
    }
}
