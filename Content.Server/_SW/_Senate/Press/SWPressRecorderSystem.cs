using System.Linq;
using Content.Server._SW._Senate.Core;
using Content.Server.Chat.Systems;
using Content.Server.Popups;
using Content.Shared.GameTicking;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Popups;
using Robust.Shared.Network;

namespace Content.Server._SW._Senate.Press;

/// <summary>Records only ordinary speech actually sent to a correspondent holding the active recorder.</summary>
public sealed class SWPressRecorderSystem : EntitySystem
{
    [Dependency] private readonly SWSenatePressSystem _press = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly MetaDataSystem _metadata = default!;
    [Dependency] private readonly SharedPointLightSystem _lights = default!;
    private readonly Dictionary<NetUserId, Recorder> _active = new();
    private readonly Dictionary<NetUserId, List<PressQuote>> _quotes = new();
    private int _nextQuote;
    private float _elapsed;
    public event Action? Changed;

    public override void Initialize()
    {
        SubscribeLocalEvent<SWPressLocalSpeechEvent>(OnSpeech);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => Reset());
    }

    public bool IsRecording(EntityUid actor, EntityUid terminal) =>
        _press.TryGetCorrespondent(actor, out var user) && _active.TryGetValue(user, out var recorder)
        && recorder.Actor == actor && recorder.Terminal == terminal && IsHeld(recorder);

    public bool Toggle(EntityUid actor, EntityUid terminal)
    {
        if (!_press.TryGetCorrespondent(actor, out var user) || !HasComp<SWPressTerminalComponent>(terminal))
            return false;
        if (_active.Remove(user, out var existing))
        {
            Stop(existing);
            Changed?.Invoke();
            return true;
        }
        var recorder = new Recorder(user, actor, terminal);
        if (!IsHeld(recorder) || _active.Values.Any(r => r.Terminal == terminal))
            return false;
        _active.Add(user, recorder);
        _metadata.SetEntityName(terminal, "● ЗАПИСЬ — редакционный датапад Голонета");
        _lights.SetEnabled(terminal, true);
        _popup.PopupEntity("Корреспондент включает запись местной речи.", actor, PopupType.Medium);
        Changed?.Invoke();
        return true;
    }

    public IReadOnlyList<PressQuote> GetQuotes(EntityUid actor)
    {
        if (!_press.TryGetCorrespondent(actor, out var user) || !_quotes.TryGetValue(user, out var quotes))
            return Array.Empty<PressQuote>();
        return quotes.ToArray();
    }

    public bool TryGetQuote(EntityUid actor, int quoteId, out PressQuote? quote)
    {
        quote = GetQuotes(actor).FirstOrDefault(q => q.Id == quoteId);
        return quote != null;
    }

    private bool IsHeld(Recorder recorder) =>
        EntityManager.EntityExists(recorder.Terminal) && _press.TryGetCorrespondent(recorder.Actor, out var user)
        && user == recorder.Owner && _hands.TryGetActiveItem(recorder.Actor, out var item) && item == recorder.Terminal;

    private void OnSpeech(SWPressLocalSpeechEvent ev)
    {
        if (ev.Message.Length == 0 || ev.Message.Length > 512 || ev.DisplayName.Length > 120)
            return;
        foreach (var actor in ev.Recipients)
        {
            if (!_press.TryGetCorrespondent(actor, out var user) || !_active.TryGetValue(user, out var recorder)
                || recorder.Actor != actor || !IsHeld(recorder))
                continue;
            if (!_quotes.TryGetValue(user, out var quotes))
                _quotes[user] = quotes = new List<PressQuote>();
            if (quotes.Count >= 30)
                continue;
            quotes.Add(new PressQuote(++_nextQuote, ev.DisplayName, ev.Message, _time.TimeString()));
            Changed?.Invoke();
        }
    }

    public override void Update(float frameTime)
    {
        _elapsed += frameTime;
        if (_elapsed < 1f) return;
        _elapsed = 0;
        foreach (var (user, recorder) in _active.ToArray())
        {
            if (IsHeld(recorder)) continue;
            _active.Remove(user);
            Stop(recorder);
            Changed?.Invoke();
        }
    }

    private void Stop(Recorder recorder)
    {
        if (EntityManager.EntityExists(recorder.Terminal))
        {
            _metadata.SetEntityName(recorder.Terminal, "редакционный датапад Голонета");
            _lights.SetEnabled(recorder.Terminal, false);
        }
    }

    private void Reset()
    {
        foreach (var recorder in _active.Values) Stop(recorder);
        _active.Clear(); _quotes.Clear(); _nextQuote = 0; _elapsed = 0;
        Changed?.Invoke();
    }

    private sealed record Recorder(NetUserId Owner, EntityUid Actor, EntityUid Terminal);
}

public sealed record PressQuote(int Id, string DisplayName, string Text, string Time);
