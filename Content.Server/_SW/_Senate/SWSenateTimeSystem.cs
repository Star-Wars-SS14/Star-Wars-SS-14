using Content.Shared.GameTicking;

namespace Content.Server._SW._Senate;

public sealed class SWSenateTimeSystem : EntitySystem
{
    public const int StartHour = 7;
    public const int EndHour = 21;
    public const float RealSecondsPerGameHour = 300f;
    private const float DayGapRealSeconds = 60f;
    private const int DayLengthMinutes = (EndHour - StartHour) * 60;

    public int Day { get; private set; } = 1;
    public bool DayActive { get; private set; } = true;

    private float _gameMinutes;
    private float _gapRemaining;

    public int CurrentHour => StartHour + Math.Clamp((int)(_gameMinutes / 60f), 0, EndHour - StartHour);
    public int CurrentMinute => (int)(_gameMinutes % 60f);

    public override void Initialize()
    {
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        // A new round is the next senate day: the clock resets to morning while the day number carries
        // over and advances (a round ending mid-day 1 makes the next round day 2).
        Day++;
        _gameMinutes = 0f;
        _gapRemaining = 0f;
        DayActive = true;
    }

    public override void Update(float frameTime)
    {
        if (!DayActive)
        {
            _gapRemaining -= frameTime;
            if (_gapRemaining <= 0f)
            {
                DayActive = true;
                Day++;
                _gameMinutes = 0f;
            }

            return;
        }

        _gameMinutes += frameTime * (60f / RealSecondsPerGameHour);
        if (_gameMinutes >= DayLengthMinutes)
        {
            _gameMinutes = DayLengthMinutes;
            DayActive = false;
            _gapRemaining = DayGapRealSeconds;
        }
    }

    public void SetTime(int hour, int minute = 0)
    {
        hour = Math.Clamp(hour, StartHour, EndHour);
        minute = Math.Clamp(minute, 0, 59);
        DayActive = true;
        _gapRemaining = 0f;
        _gameMinutes = Math.Clamp((hour - StartHour) * 60 + minute, 0, DayLengthMinutes);
    }

    public string TimeString()
    {
        if (!DayActive)
            return $"День {Day}: перерыв";

        var totalMinutes = (int)_gameMinutes;
        var hour = StartHour + totalMinutes / 60;
        var minute = totalMinutes % 60;
        return $"День {Day}, {hour:00}:{minute:00}";
    }
}
