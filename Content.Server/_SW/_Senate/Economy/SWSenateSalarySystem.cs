using Content.Server._SW._Senate.Core;
using Content.Shared._SW._Senate.Ministries;
using Content.Shared.GameTicking;
using Robust.Shared.Prototypes;

namespace Content.Server._SW._Senate.Economy;

// Hourly paycheck for every tracked office - credited to the same personal account the shadow
// economy uses, but through SWCorruptionSystem.AddAccount so it never touches the corruption index.
// Staff roles (usher/guard/doctor) are paid separately by SWSenateStaffSystem, keyed off shift
// clock-in rather than a take-office console.
public sealed class SWSenateSalarySystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly SWSenateStateSystem _senate = default!;
    [Dependency] private readonly SWMinistrySystem _ministry = default!;
    [Dependency] private readonly SWSenateCourtSystem _court = default!;
    [Dependency] private readonly SWCorruptionSystem _corruption = default!;

    private const int SenatorPay = 50;
    private const int DeputyPay = 70; // department/bureau heads
    private const int MinisterPay = 100;
    private const int JudicialPay = 90;
    private const int SpeakerPay = 120;
    private const int ChancellorPay = 150;

    private int _lastPaidDay = -1;
    private int _lastPaidHour = -1;

    public override void Initialize()
    {
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _lastPaidDay = -1;
        _lastPaidHour = -1;
    }

    public override void Update(float frameTime)
    {
        if (!_time.ModeActive || !_time.DayActive)
            return;

        if (_time.Day == _lastPaidDay && _time.CurrentHour == _lastPaidHour)
            return;

        _lastPaidDay = _time.Day;
        _lastPaidHour = _time.CurrentHour;
        PayAll();
    }

    private void PayAll()
    {
        foreach (var holder in _senate.SectorHolders.Values)
            _corruption.AddAccount(holder, SenatorPay);

        if (_senate.Chancellor is { } chancellor)
            _corruption.AddAccount(chancellor, ChancellorPay);

        if (_senate.Speaker is { } speaker)
            _corruption.AddAccount(speaker, SpeakerPay);

        foreach (var (ministryId, holder) in _ministry.Holders)
        {
            var pay = _proto.TryIndex<SWMinistryPrototype>(ministryId, out var ministry) && ministry.Kind == SWMinistryKind.Ministry
                ? MinisterPay
                : DeputyPay;
            _corruption.AddAccount(holder, pay);
        }

        if (_court.Chief is { } chief)
            _corruption.AddAccount(chief, JudicialPay);

        foreach (var judge in _court.Judges)
            _corruption.AddAccount(judge, JudicialPay);

        if (_court.Prosecutor is { } prosecutor)
            _corruption.AddAccount(prosecutor, JudicialPay);

        if (_court.Defender is { } defender)
            _corruption.AddAccount(defender, JudicialPay);
    }
}
