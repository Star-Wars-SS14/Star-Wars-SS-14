using Content.Server._SW._Senate.GameTicking;
using Content.Shared.GameTicking;

namespace Content.Server._SW._Senate.Core;

public sealed class SWSenateTimeSystem : EntitySystem
{
    public const int StartHour = 7;
    public const int EndHour = 21;
    public const float RealSecondsPerGameHour = 240f;
    private const float DayGapRealSeconds = 300f;
    private const int DayLengthMinutes = (EndHour - StartHour) * 60;

    public int Day { get; private set; } = 1;
    public bool DayActive { get; private set; } = true;
    public bool ModeActive { get; set; }

    // Accumulate in double: float rounding at 30 ticks/s shortened a full day
    // enough to lose the final quarter-hour economy step over a three-day round.
    private double _gameMinutes;
    private float _gapRemaining;

    public int CurrentHour => StartHour + Math.Clamp((int)(_gameMinutes / 60f), 0, EndHour - StartHour);
    public int CurrentMinute => (int)(_gameMinutes % 60f);
    public int AbsoluteHour => Day * 24 + CurrentHour;
    public double CalendarGameMinutes => Day * 1440d + StartHour * 60d + _gameMinutes;
    public double ActiveGameMinutes { get; private set; }
    public double ActiveGameHours => ActiveGameMinutes / 60d;

    // Simulation systems run before the clock. Clip their elapsed time at the day boundary.
    public float ActiveSeconds(float seconds) => ModeActive && DayActive
        ? (float) Math.Clamp(seconds, 0d, Math.Max(0d, (DayLengthMinutes - _gameMinutes) * RealSecondsPerGameHour / 60d))
        : 0f;

    public override void Initialize()
    {
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        // A new round is the next senate day: the clock resets to morning while the day number carries
        // over and advances (a round ending mid-day 1 makes the next round day 2).
        Day++;
        ActiveGameMinutes = 0d;
        _gameMinutes = 0f;
        _gapRemaining = 0f;
        DayActive = true;
        ModeActive = false;
    }

    public override void Update(float frameTime)
    {
        if (!ModeActive)
            return;

        if (!DayActive)
        {
            _gapRemaining -= frameTime;
            if (_gapRemaining <= 0f)
            {
                DayActive = true;
                Day++;
                _gameMinutes = 0f;
                RaiseLocalEvent(new SWSenateCustomAnnouncementEvent("Сенат Республики",
                    $"Наступил день {Day}. Ведомства Республики возобновляют работу."));
            }

            return;
        }

        var activeMinutes = ActiveSeconds(frameTime) * (60d / RealSecondsPerGameHour);
        ActiveGameMinutes += activeMinutes;
        _gameMinutes += activeMinutes;
        if (_gameMinutes >= DayLengthMinutes)
        {
            _gameMinutes = DayLengthMinutes;
            RaiseLocalEvent(new SWSenateDayClosedEvent(FinalNight()));
            DayActive = false;
            _gapRemaining = DayGapRealSeconds;
            if (!FinalNight())
                RaiseLocalEvent(new SWSenateCustomAnnouncementEvent("Сенат Республики",
                    "Наступила ночь. Ведомства Республики возобновят работу в 07:00."));
        }
    }

    // The senate rule ends the round the moment the last day closes, so that night never plays out.
    private bool FinalNight()
    {
        var query = EntityQueryEnumerator<SWSenateRuleComponent>();
        while (query.MoveNext(out var rule))
        {
            if (Day - rule.StartDay >= rule.RoundDays - 1)
                return true;
        }

        return false;
    }

    public void SetTime(int hour, int minute = 0)
    {
        hour = Math.Clamp(hour, StartHour, EndHour);
        minute = Math.Clamp(minute, 0, 59);
        DayActive = true;
        _gapRemaining = 0f;
        _gameMinutes = Math.Clamp((hour - StartHour) * 60 + minute, 0, DayLengthMinutes);
    }

    public void SetDateTime(int day, int hour, int minute = 0)
    {
        Day = Math.Max(1, day);
        SetTime(hour, minute);
    }

    public string TimeString()
    {
        if (!DayActive)
            return $"День {Day}: ночь";

        var totalMinutes = (int)_gameMinutes;
        var hour = StartHour + totalMinutes / 60;
        var minute = totalMinutes % 60;
        return $"День {Day}, {hour:00}:{minute:00}";
    }
}
