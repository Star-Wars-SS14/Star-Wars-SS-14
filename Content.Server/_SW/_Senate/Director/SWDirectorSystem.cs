using System.Linq;
using Content.Server._SW._Senate.Core;
using Content.Server.Chat.Systems;
using Content.Shared._SW._Senate.Director;
using Content.Shared._SW._Senate.Sectors;
using Content.Shared.GameTicking;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._SW._Senate.Director;

// The Empire as a playable faction. Three modes: Off (calm republic-only round), Autopilot (the
// system spends imperial influence itself - the default), Manual (an admin at the helm via the
// swdirector EUI). Influence accrues per game hour and grows when the Republic is weak - a strong
// Republic starves the Empire, which is the whole counterplay of v1.
public sealed class SWDirectorSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly SWSenateStateSystem _senate = default!;
    [Dependency] private readonly SWMinistryDefenseSystem _defense = default!;
    [Dependency] private readonly SWCorruptionSystem _corruption = default!;
    [Dependency] private readonly ChatSystem _chat = default!;

    public const int InfluenceCap = 100;
    public const int CostManeuvers = 20;
    public const int CostProvocation = 25;
    public const int CostSanction = 30;
    public const int CostBlockade = 50;

    private const float StartInfluence = 20f;
    private const float BaseIncomePerHour = 8f;
    private const float AutopilotReserve = 10f;
    private const float AutopilotActChance = 0.4f;

    private const float ManeuversBorderDelta = 12f;
    private const float ProvocationBorderDelta = 6f;
    private const float ProvocationSocietyHit = 4f;
    private const int SanctionHours = 6;
    private const int BlockadeHours = 4;
    private const float SanctionEconomyPerMinute = 0.06f;
    private const float SanctionIndustryPerMinute = 0.04f;
    private const float BlockadeEconomyPerMinute = 0.05f;

    private static readonly Color ImperialColor = Color.FromHex("#C0392B");
    private const string ImperialSender = "Империя Ситов";

    public SWDirectorMode Mode { get; private set; } = SWDirectorMode.Autopilot;
    public NetUserId? Controller { get; private set; }
    public string ControllerName { get; private set; } = string.Empty;

    public bool AutoAntagsAllowed => Mode == SWDirectorMode.Autopilot;

    public event Action? Updated;

    private float _influence = StartInfluence;
    private readonly Dictionary<string, float> _sanctions = new();
    private float _blockade;
    private readonly List<string> _chronicle = new();
    private readonly List<SWDiploEntry> _diplomacy = new();
    private float _minuteAccum;
    private int _lastHour = -1;

    public override void Initialize()
    {
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        Mode = SWDirectorMode.Autopilot;
        Controller = null;
        ControllerName = string.Empty;
        _influence = StartInfluence;
        _sanctions.Clear();
        _blockade = 0f;
        _chronicle.Clear();
        _diplomacy.Clear();
        _minuteAccum = 0f;
        _lastHour = -1;
    }

    public override void Update(float frameTime)
    {
        if (!_time.ModeActive || !_time.DayActive)
            return;

        _minuteAccum += frameTime * (60f / SWSenateTimeSystem.RealSecondsPerGameHour);
        if (_minuteAccum < 1f)
            return;

        var minutes = _minuteAccum;
        _minuteAccum = 0f;

        TickEffects(minutes);

        if (Mode != SWDirectorMode.Off)
            _influence = Math.Min(_influence + IncomePerHour() / 60f * minutes, InfluenceCap);

        var hour = _time.AbsoluteHour;
        if (hour != _lastHour)
        {
            _lastHour = hour;
            AutopilotTick();
            Updated?.Invoke();
        }
    }

    private void TickEffects(float minutes)
    {
        foreach (var (sectorId, left) in _sanctions.ToList())
        {
            _senate.NudgeSectorGlobal(sectorId, "SWGlobalEconomy", -SanctionEconomyPerMinute * minutes);
            _senate.NudgeSectorGlobal(sectorId, "SWGlobalIndustry", -SanctionIndustryPerMinute * minutes);

            var remaining = left - minutes;
            if (remaining > 0f)
            {
                _sanctions[sectorId] = remaining;
                continue;
            }

            _sanctions.Remove(sectorId);
            Announce($"Империя объявила об окончании санкций против сектора {SectorName(sectorId)}.");
            AddChronicle($"санкции против сектора {SectorName(sectorId)} истекли");
        }

        if (_blockade > 0f)
        {
            foreach (var sectorId in _senate.SectorHolders.Keys)
                _senate.NudgeSectorGlobal(sectorId, "SWGlobalEconomy", -BlockadeEconomyPerMinute * minutes);

            _blockade -= minutes;
            if (_blockade <= 0f)
            {
                _blockade = 0f;
                Announce("Империя сняла блокаду республиканских торговых маршрутов.");
                AddChronicle("торговая блокада истекла");
            }
        }
    }

    public float IncomePerHour()
    {
        var income = BaseIncomePerHour;
        var globals = _senate.GetRepublicGlobals();

        if (globals.TryGetValue("SWGlobalDefense", out var defense))
            income += Math.Clamp((50f - defense) * 0.08f, 0f, 4f);
        if (globals.TryGetValue("SWGlobalForeign", out var foreign))
            income += Math.Clamp((50f - foreign) * 0.08f, 0f, 4f);

        income += Math.Clamp((_corruption.GlobalIndex() - 25) * 0.05f, 0f, 4f);
        return income;
    }

    public string IncomeBreakdown()
    {
        var globals = _senate.GetRepublicGlobals();
        var parts = new List<string> { $"база {BaseIncomePerHour:0.#}" };

        if (globals.TryGetValue("SWGlobalDefense", out var defense) && defense < 50f)
            parts.Add($"оборона +{Math.Clamp((50f - defense) * 0.08f, 0f, 4f):0.#}");
        if (globals.TryGetValue("SWGlobalForeign", out var foreign) && foreign < 50f)
            parts.Add($"внешняя политика +{Math.Clamp((50f - foreign) * 0.08f, 0f, 4f):0.#}");
        if (_corruption.GlobalIndex() > 25)
            parts.Add($"коррупция +{Math.Clamp((_corruption.GlobalIndex() - 25) * 0.05f, 0f, 4f):0.#}");

        return string.Join(", ", parts);
    }

    public void SetMode(SWDirectorMode mode, NetUserId? controller, string controllerName)
    {
        Mode = mode;
        Controller = mode == SWDirectorMode.Manual ? controller : null;
        ControllerName = mode == SWDirectorMode.Manual ? controllerName : string.Empty;

        AddChronicle(mode switch
        {
            SWDirectorMode.Off => "Империя отключена (спокойный раунд)",
            SWDirectorMode.Autopilot => "Империя переведена на автопилот",
            _ => $"управление Империей взял {controllerName}",
        });
        Updated?.Invoke();
    }

    public bool TryAction(SWDirectorActionKind kind, string sectorId, out string result)
    {
        result = string.Empty;
        if (Mode == SWDirectorMode.Off)
        {
            result = "Империя отключена.";
            return false;
        }

        switch (kind)
        {
            case SWDirectorActionKind.Maneuvers:
                if (!Spend(CostManeuvers, ref result))
                    return false;
                _defense.AddBorder(ManeuversBorderDelta);
                _defense.AddLog("Разведка: имперский флот стягивается к границе.");
                Announce("Имперский флот начал широкомасштабные манёвры у границы Республики.");
                AddChronicle("флотские манёвры у границы");
                break;

            case SWDirectorActionKind.Provocation:
                if (!ValidSector(sectorId, ref result) || !Spend(CostProvocation, ref result))
                    return false;
                _defense.AddBorder(ProvocationBorderDelta);
                _senate.NudgeSectorGlobal(sectorId, "SWGlobalSociety", -ProvocationSocietyHit);
                Announce($"Имперские корабли нарушили границу сектора {SectorName(sectorId)}. Зафиксированы провокационные манёвры.");
                AddChronicle($"провокация в секторе {SectorName(sectorId)}");
                break;

            case SWDirectorActionKind.Sanction:
                if (!ValidSector(sectorId, ref result))
                    return false;
                if (_sanctions.ContainsKey(sectorId))
                {
                    result = "Санкции против этого сектора уже действуют.";
                    return false;
                }
                if (!Spend(CostSanction, ref result))
                    return false;
                _sanctions[sectorId] = SanctionHours * 60f;
                Announce($"Империя вводит торговые санкции против сектора {SectorName(sectorId)}.");
                AddChronicle($"санкции против сектора {SectorName(sectorId)}");
                break;

            case SWDirectorActionKind.LiftSanction:
                if (!_sanctions.Remove(sectorId))
                {
                    result = "Санкций против этого сектора нет.";
                    return false;
                }
                Announce($"Империя снимает санкции с сектора {SectorName(sectorId)}.");
                AddChronicle($"санкции с сектора {SectorName(sectorId)} сняты досрочно");
                break;

            case SWDirectorActionKind.Blockade:
                if (_blockade > 0f)
                {
                    result = "Блокада уже действует.";
                    return false;
                }
                if (!Spend(CostBlockade, ref result))
                    return false;
                _blockade = BlockadeHours * 60f;
                Announce("Империя объявляет блокаду республиканских торговых маршрутов.");
                AddChronicle("торговая блокада Республики");
                break;
        }

        Updated?.Invoke();
        return true;
    }

    private bool Spend(int cost, ref string result)
    {
        if (_influence < cost)
        {
            result = $"Недостаточно влияния: нужно {cost}.";
            return false;
        }

        _influence -= cost;
        return true;
    }

    private bool ValidSector(string sectorId, ref string result)
    {
        if (_senate.SectorHolders.ContainsKey(sectorId))
            return true;

        result = "Сектор не занят.";
        return false;
    }

    private void AutopilotTick()
    {
        if (Mode != SWDirectorMode.Autopilot || !_random.Prob(AutopilotActChance))
            return;

        var candidates = new List<(SWDirectorActionKind Kind, string Sector, int Weight)>();
        var claimed = _senate.SectorHolders.Keys.ToList();

        if (_influence >= CostManeuvers + AutopilotReserve && _defense.BorderPressure < 70)
            candidates.Add((SWDirectorActionKind.Maneuvers, string.Empty, 3));

        if (claimed.Count > 0 && _influence >= CostProvocation + AutopilotReserve)
            candidates.Add((SWDirectorActionKind.Provocation, _random.Pick(claimed), 2));

        var unsanctioned = claimed.Where(s => !_sanctions.ContainsKey(s)).ToList();
        if (unsanctioned.Count > 0 && _influence >= CostSanction + AutopilotReserve)
            candidates.Add((SWDirectorActionKind.Sanction, _random.Pick(unsanctioned), 2));

        if (_blockade <= 0f && _influence >= CostBlockade + AutopilotReserve)
            candidates.Add((SWDirectorActionKind.Blockade, string.Empty, 1));

        if (candidates.Count == 0)
            return;

        var total = candidates.Sum(c => c.Weight);
        var roll = _random.Next(total);
        foreach (var (kind, sector, weight) in candidates)
        {
            roll -= weight;
            if (roll >= 0)
                continue;

            TryAction(kind, sector, out _);
            return;
        }
    }

    public bool SendImperialNote(string text, bool isPublic)
    {
        if (Mode == SWDirectorMode.Off || string.IsNullOrWhiteSpace(text))
            return false;

        text = text.Trim();
        _diplomacy.Add(new SWDiploEntry
        {
            FromEmpire = true,
            Stamp = Stamp(),
            Text = text,
            Public = isPublic,
        });

        if (isPublic)
            Announce(text);

        AddChronicle(isPublic ? $"публичная нота: «{Shorten(text)}»" : $"тайная нота Канцлеру: «{Shorten(text)}»");
        Updated?.Invoke();
        return true;
    }

    public bool AddRepublicReply(string author, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        text = text.Trim();
        _diplomacy.Add(new SWDiploEntry
        {
            FromEmpire = false,
            Stamp = $"{Stamp()} · {author}",
            Text = text,
        });

        AddChronicle($"ответ Республики ({author}): «{Shorten(text)}»");
        Updated?.Invoke();
        return true;
    }

    public List<SWDiploEntry> BuildDiplomacy() => new(_diplomacy);

    private string Stamp() => $"День {_time.Day}, {_time.CurrentHour:00}:00";

    private static string Shorten(string text) =>
        text.Length <= 60 ? text : text[..57] + "...";

    private void Announce(string text)
    {
        _chat.DispatchGlobalAnnouncement(text, ImperialSender, colorOverride: ImperialColor);
    }

    private void AddChronicle(string text)
    {
        _chronicle.Add($"{Stamp()} — {text}");
    }

    public SWDirectorEuiState BuildEuiState(NetUserId viewer)
    {
        var state = new SWDirectorEuiState
        {
            Mode = Mode,
            ControllerName = ControllerName,
            IAmController = Controller == viewer,
            Influence = (int) MathF.Round(_influence),
            InfluenceCap = InfluenceCap,
            IncomeLine = $"+{IncomePerHour():0.#}/ч ({IncomeBreakdown()})",
            BorderPressure = _defense.BorderPressure,
            BorderStatus = _defense.BorderStatus(),
            BlockadeActive = _blockade > 0f,
            BlockadeLabel = _blockade > 0f ? $"ещё ~{Math.Ceiling(_blockade / 60f)} ч" : string.Empty,
            CostManeuvers = CostManeuvers,
            CostProvocation = CostProvocation,
            CostSanction = CostSanction,
            CostBlockade = CostBlockade,
        };

        foreach (var (sectorId, holder) in _senate.SectorHolders.OrderBy(kv => kv.Key))
        {
            var sanctioned = _sanctions.TryGetValue(sectorId, out var left);
            state.Sectors.Add(new SWDirectorSectorEntry
            {
                Id = sectorId,
                Name = SectorName(sectorId),
                Holder = holder,
                Sanctioned = sanctioned,
                SanctionLabel = sanctioned ? $"санкции: ещё ~{Math.Ceiling(left / 60f)} ч" : string.Empty,
            });
        }

        for (var i = Math.Max(0, _chronicle.Count - 15); i < _chronicle.Count; i++)
            state.Chronicle.Add(_chronicle[i]);
        state.Chronicle.Reverse();

        state.Diplomacy = BuildDiplomacy();

        return state;
    }

    private string SectorName(string sectorId) =>
        _proto.TryIndex<SWSectorPrototype>(sectorId, out var sector) ? sector.Name : sectorId;
}
