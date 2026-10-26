using System.Linq;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Sectors;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;
using Content.Server._SW._Senate.Core;
using Content.Shared.GameTicking;

namespace Content.Server._SW._Senate.Consoles;

public sealed class SWStatisticsConsoleSystem : EntitySystem
{
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly SWSenateStateSystem _senate = default!;
    [Dependency] private readonly SWSenateCrisisSystem _crisis = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly SWMinistryDefenseSystem _defense = default!;
    [Dependency] private readonly SWMinistryInternalSecuritySystem _security = default!;
    [Dependency] private readonly SWMinistryHealthSystem _health = default!;
    [Dependency] private readonly SWMinistryEconomySystem _economy = default!;
    [Dependency] private readonly SWMinistryFinanceSystem _finance = default!;
    [Dependency] private readonly SWMinistryJusticeSystem _justice = default!;
    [Dependency] private readonly SWCorruptionSystem _corruption = default!;
    [Dependency] private readonly SWEmergencyPowersSystem _powers = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    private const float UpdateInterval = 3f;
    private float _accumulator;
    private readonly SWStatisticsHistoryBuffer _history = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<SWStatisticsConsoleComponent, BoundUIOpenedEvent>(OnOpened);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ =>
        {
            _history.Clear();
            _accumulator = 0f;
        });
    }

    private void OnOpened(EntityUid uid, SWStatisticsConsoleComponent comp, BoundUIOpenedEvent args)
    {
        UpdateUi((uid, comp));
    }

    public override void Update(float frameTime)
    {
        if (!_time.ModeActive)
            return;

        _accumulator += frameTime;
        if (_accumulator < UpdateInterval)
            return;

        _accumulator = 0f;

        _history.Record(_time.AbsoluteHour * 60 + _time.CurrentMinute, _senate.BuildGlobalEntries());

        var query = EntityQueryEnumerator<SWStatisticsConsoleComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (_ui.IsUiOpen(uid, SWStatisticsConsoleUiKey.Key))
                UpdateUi((uid, comp));
        }
    }

    private void UpdateUi(Entity<SWStatisticsConsoleComponent> ent)
    {
        var globals = _senate.BuildGlobalEntries();

        var sectors = _senate.BuildSectorEntries();

        var stance = _defense.Stance;
        var state = new SWStatisticsConsoleState(globals, sectors)
        {
            Crises = _crisis.BuildCrises(null, false),
            MapCrises = BuildMapCrises(),
            ActiveLaws = _senate.BuildActiveLaws(),
            GameTime = _time.TimeString(),
            SampleMinute = _time.AbsoluteHour * 60 + _time.CurrentMinute,
            History = _history.Snapshot(),
            DefconLevel = SWMinistryDefenseSystem.DefconLevel(stance),
            DefconName = SWMinistryDefenseSystem.StanceName(stance),
            DefconColor = SWMinistryDefenseSystem.StanceColor(stance),
            BorderPressure = _defense.BorderPressure,
            BorderStatus = _defense.BorderStatus(),
            DefconEffects = _defense.BuildStanceEffects(stance),
            PostureName = SWMinistryInternalSecuritySystem.PostureName(_security.Posture),
            PostureColor = SWMinistryInternalSecuritySystem.PostureColor(_security.Posture),
            Threat = _security.Threat,
            ThreatStatus = _security.ThreatStatus(),
            Unrest = _security.Unrest,
            UnrestStatus = _security.UnrestStatus(),
            PostureEffects = _security.BuildPostureEffects(_security.Posture),
            PathogenActive = _health.PathogenActive,
            PandemicStatus = _health.PandemicStatus(),
            PandemicColor = _health.PandemicColor(),
            PathogenName = _health.PathogenName,
            PathogenStrain = _health.PathogenStrain + 1,
            InfectionAvg = (int) MathF.Round(_health.InfectionAvg()),
            Immunity = _health.Immunity,
            Research = _health.Research,
            EconPhaseName = SWMinistryEconomySystem.PhaseName(_economy.Phase),
            EconPhaseColor = SWMinistryEconomySystem.PhaseColor(_economy.Phase),
            Growth = _economy.Growth,
            Overheat = _economy.Overheat,
            OverheatStatus = _economy.OverheatStatus(),
            KeyRate = _finance.KeyRate,
            Inflation = _finance.Inflation,
            InflationStatus = _finance.InflationStatus(),
            Debt = _finance.Debt,
            CreditRating = _finance.CreditRating(),
            RatingColor = _finance.RatingColor(),
            Backlog = _justice.Backlog,
            BacklogStatus = _justice.BacklogStatus(),
            JusticeStanceName = SWMinistryJusticeSystem.StanceName(_justice.Stance),
            JusticeStanceColor = SWMinistryJusticeSystem.StanceColor(_justice.Stance),
            CaseCount = _justice.CaseCount,
            CorruptionIndex = _corruption.GlobalIndex(),
            CorruptionStatus = _corruption.GlobalStatus(),
            PowersActive = _powers.Active,
            EmergencyLevelName = _powers.LevelName(),
            Authority = _powers.Authority,
            Legitimacy = _powers.Legitimacy,
        };

        _ui.SetUiState(ent.Owner, SWStatisticsConsoleUiKey.Key, state);
    }

    public List<SWMapCrisisEntry> BuildMapCrises()
    {
        var entries = _crisis.BuildMapCrises();
        entries.AddRange(_defense.BuildPublicMapCrises());
        return entries.OrderByDescending(c => c.Severity).ThenBy(c => c.Id).ToList();
    }
}
