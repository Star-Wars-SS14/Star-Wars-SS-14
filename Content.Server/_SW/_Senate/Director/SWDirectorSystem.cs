using System.Linq;
using Content.Server._SW._Senate.Antag;
using Content.Server._SW._Senate.Core;
using Content.Server._SW._Senate.Economy;
using Content.Server.Chat.Systems;
using Content.Server.GameTicking;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Crises;
using Content.Shared._SW._Senate.Dilemmas;
using Content.Shared._SW._Senate.Director;
using Content.Shared._SW._Senate.Ministries;
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
public sealed partial class SWDirectorSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly SWSenateStateSystem _senate = default!;
    [Dependency] private readonly SWMinistryDefenseSystem _defense = default!;
    [Dependency] private readonly SWCorruptionSystem _corruption = default!;
    [Dependency] private readonly SWSenateCrisisSystem _crisis = default!;
    [Dependency] private readonly SWMinistryEconomySystem _economyEvents = default!;
    [Dependency] private readonly SWSenateDilemmaSystem _dilemma = default!;
    [Dependency] private readonly SWImperialStrikeSystem _strike = default!;
    [Dependency] private readonly SWMercSystem _merc = default!;
    [Dependency] private readonly ChatSystem _chat = default!;

    public const int InfluenceCap = 100;
    public const int CostManeuvers = 20;
    public const int CostProvocation = 25;
    public const int CostSanction = 30;
    public const int CostBlockade = 50;
    public const int CostCrisis = 40;
    public const int CostRepublicCrisis = 50;
    public const int CostDilemma = 15;
    public const int CostStrike = 60;
    public const int CostMerc = 45;

    private const float StartInfluence = 20f;
    private const float BaseIncomePerHour = 8f;
    private const float AutopilotReserve = 10f;
    private const float AutopilotActChance = 0.4f;

    private const int SanctionHours = 6;
    private const float SanctionEconomyPerMinute = 0.06f;
    private const float SanctionIndustryPerMinute = 0.04f;

    private static readonly Color ImperialColor = Color.FromHex("#C0392B");
    private const string ImperialSender = "Империя Ситов";

    public SWDirectorMode Mode { get; private set; } = SWDirectorMode.Autopilot;
    public NetUserId? Controller { get; private set; }
    public string ControllerName { get; private set; } = string.Empty;

    public bool AutoAntagsAllowed => Mode == SWDirectorMode.Autopilot;

    public event Action? Updated;

    private float _influence = StartInfluence;
    private readonly Dictionary<string, float> _sanctions = new();
    private readonly List<string> _chronicle = new();
    private readonly List<SWDiploEntry> _diplomacy = new();
    private float _minuteAccum;
    private int _lastHour = -1;

    public override void Initialize()
    {
        UpdatesBefore.Add(typeof(SWSenateTimeSystem));
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        SubscribeLocalEvent<RoundEndTextAppendEvent>(OnRoundEnd);
    }

    // The full imperial ledger - covert ops included - goes public once the round is over.
    private void OnRoundEnd(RoundEndTextAppendEvent args)
    {
        if (_chronicle.Count == 0)
            return;

        args.AddLine(string.Empty);
        args.AddLine("[bold][color=#C0392B]Хроника Империи[/color][/bold]");
        foreach (var line in _chronicle)
            args.AddLine(line);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        Mode = SWDirectorMode.Autopilot;
        Controller = null;
        ControllerName = string.Empty;
        _influence = StartInfluence;
        _sanctions.Clear();
        _chronicle.Clear();
        _diplomacy.Clear();
        LastNotice = string.Empty;
        _minuteAccum = 0f;
        _lastHour = -1;
    }

    public override void Update(float frameTime)
    {
        frameTime = _time.ActiveSeconds(frameTime);
        if (frameTime <= 0f)
            return;
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
        }
        Updated?.Invoke();
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
        _defense.ImperialThreatsEnabled = mode != SWDirectorMode.Off;
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

    public string LastNotice { get; private set; } = string.Empty;

    public bool TryAction(SWDirectorActionKind kind, string sectorId, out string result) =>
        TryAction(kind, sectorId, string.Empty, string.Empty, out result);

    public bool TryAction(SWDirectorActionKind kind, string sectorId, string param, string target, out string result)
    {
        var ok = DoAction(kind, sectorId, param, target, out result);
        if (result.Length > 0)
            LastNotice = result;
        else if (ok)
            LastNotice = string.Empty;
        return ok;
    }

    private bool DoAction(SWDirectorActionKind kind, string sectorId, string param, string target, out string result)
    {
        result = string.Empty;
        if (!_time.ModeActive || !_time.DayActive)
        {
            result = "Операции доступны только в рабочее время Сената.";
            return false;
        }

        if (Mode == SWDirectorMode.Off)
        {
            result = "Империя отключена.";
            return false;
        }

        switch (kind)
        {
            case SWDirectorActionKind.EconomyIncident:
                if (Mode != SWDirectorMode.Manual)
                { result = "Экономическое событие доступно только в ручном режиме."; return false; }
                if (!_economyEvents.CreateDirectorEconomyIncident(param, target, out result)) return false;
                AddChronicle(result);
                break;

            case SWDirectorActionKind.Maneuvers:
                if (!CreateTacticalPressure(string.Empty, false, CostManeuvers, ref result))
                    return false;
                Announce("Имперский флот начал широкомасштабные манёвры у границы Республики.");
                AddChronicle("флотские манёвры у границы");
                break;

            case SWDirectorActionKind.Provocation:
                if (!CreateTacticalPressure(sectorId, false, CostProvocation, ref result))
                    return false;
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
                if (!CreateTacticalPressure(string.Empty, true, CostBlockade, ref result))
                    return false;
                Announce("Имперский флот установил блокаду на приграничном гиперпути.");
                AddChronicle("локальная блокада гиперпути");
                break;

            case SWDirectorActionKind.Crisis:
            {
                if (!ValidSector(sectorId, ref result))
                    return false;
                var pool = _proto.EnumeratePrototypes<SWCrisisPrototype>().Where(c => !c.Republic).ToList();
                if (pool.Count == 0 || _influence < CostCrisis)
                {
                    result = pool.Count == 0 ? "Нет подходящих кризисов." : $"Недостаточно влияния: нужно {CostCrisis}.";
                    return false;
                }
                if (!_crisis.AdminSpawn(sectorId, _random.Pick(pool).ID))
                {
                    result = "Кризис не удалось спровоцировать.";
                    return false;
                }
                _influence -= CostCrisis;
                AddChronicle($"тайно: спровоцирован кризис в секторе {SectorName(sectorId)}");
                result = "Кризис спровоцирован.";
                break;
            }

            case SWDirectorActionKind.RepublicCrisis:
            {
                var pool = _proto.EnumeratePrototypes<SWCrisisPrototype>().Where(c => c.Republic).ToList();
                if (pool.Count == 0 || _influence < CostRepublicCrisis)
                {
                    result = pool.Count == 0 ? "Нет подходящих кризисов." : $"Недостаточно влияния: нужно {CostRepublicCrisis}.";
                    return false;
                }
                if (!_crisis.AdminSpawn(null, _random.Pick(pool).ID))
                {
                    result = "Кризис не удалось спровоцировать.";
                    return false;
                }
                _influence -= CostRepublicCrisis;
                AddChronicle("тайно: спровоцирован республиканский кризис");
                result = "Республиканский кризис спровоцирован.";
                break;
            }

            case SWDirectorActionKind.Dilemma:
            {
                if (!ValidSector(sectorId, ref result))
                    return false;
                if (!SpawnDilemma(SWDilemmaScope.Sectoral, sectorId, sectorId, ref result))
                    return false;
                AddChronicle($"тайно: подброшена дилемма сектору {SectorName(sectorId)}");
                result = "Дилемма подброшена.";
                break;
            }

            case SWDirectorActionKind.RepublicDilemma:
            {
                if (!SpawnDilemma(SWDilemmaScope.Republic, SWSenateDilemmaSystem.RepublicOwner, null, ref result))
                    return false;
                AddChronicle("тайно: подброшена дилемма Канцлеру");
                result = "Дилемма подброшена Канцлеру.";
                break;
            }

            case SWDirectorActionKind.Strike:
            {
                if (_influence < CostStrike)
                {
                    result = $"Недостаточно влияния: нужно {CostStrike}.";
                    return false;
                }
                SWStrikeTask? task = param switch
                {
                    "kidnap" => SWStrikeTask.Kidnap,
                    "kill" => SWStrikeTask.Kill,
                    "capture" => SWStrikeTask.Capture,
                    _ => null,
                };
                if (!_strike.TryLaunch(task, target.Length > 0 ? target : null, out var strikeError))
                {
                    result = strikeError;
                    return false;
                }
                _influence -= CostStrike;
                var taskLabel = param switch
                {
                    "kidnap" => "похищение",
                    "kill" => "убийство",
                    "capture" => "захват",
                    _ => "случайная задача",
                };
                AddChronicle($"тайно: заброшен диверсионный отряд ({taskLabel}{(target.Length > 0 ? $", цель {target}" : "")})");
                result = "Диверсионный отряд заброшен.";
                break;
            }

            case SWDirectorActionKind.Merc:
            {
                if (_influence < CostMerc)
                {
                    result = $"Недостаточно влияния: нужно {CostMerc}.";
                    return false;
                }
                var kind2 = param == "kidnap" ? SWMercContractKind.Kidnap : SWMercContractKind.Kill;
                if (target.Length == 0)
                {
                    result = "Не выбрана цель.";
                    return false;
                }
                if (!_merc.TryOrder(kind2, target, out var mercError))
                {
                    result = mercError;
                    return false;
                }
                _influence -= CostMerc;
                AddChronicle($"тайно: нанят наёмник ({(kind2 == SWMercContractKind.Kidnap ? "похищение" : "убийство")}, цель {target})");
                result = "Контракт наёмника оформлен.";
                break;
            }
        }

        Updated?.Invoke();
        return true;
    }

    private bool SpawnDilemma(SWDilemmaScope scope, string owner, string? sectorId, ref string result)
    {
        var pool = _proto.EnumeratePrototypes<SWDilemmaPrototype>()
            .Where(d => d.Scope == scope && d.Conditions.Count == 0 && !d.Budget)
            .ToList();

        if (pool.Count == 0)
        {
            result = "Нет подходящих дилемм.";
            return false;
        }

        if (_influence < CostDilemma)
        {
            result = $"Недостаточно влияния: нужно {CostDilemma}.";
            return false;
        }

        _dilemma.AdminAddDilemma(scope, owner, sectorId, _random.Pick(pool).ID);
        _influence -= CostDilemma;
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

        if (_defense.MinisterActive && _influence >= CostManeuvers + AutopilotReserve && _defense.BorderPressure < 70)
            candidates.Add((SWDirectorActionKind.Maneuvers, string.Empty, 3));

        if (_defense.MinisterActive && claimed.Count > 0 && _influence >= CostProvocation + AutopilotReserve)
            candidates.Add((SWDirectorActionKind.Provocation, _random.Pick(claimed), 2));

        var unsanctioned = claimed.Where(s => !_sanctions.ContainsKey(s)).ToList();
        if (unsanctioned.Count > 0 && _influence >= CostSanction + AutopilotReserve)
            candidates.Add((SWDirectorActionKind.Sanction, _random.Pick(unsanctioned), 2));

        if (_defense.MinisterActive && _influence >= CostBlockade + AutopilotReserve)
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
            Stamp = $"{Stamp()}, {author}",
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
            DefenseMap = _defense.BuildMapState(includeOrderOptions: false),
            Mode = Mode,
            ControllerName = ControllerName,
            IAmController = Controller == viewer,
            Influence = (int) MathF.Round(_influence),
            InfluenceCap = InfluenceCap,
            IncomeLine = $"+{IncomePerHour():0.#}/ч ({IncomeBreakdown()})",
            BorderPressure = _defense.BorderPressure,
            BorderStatus = _defense.BorderStatus(),
            BlockadeActive = false,
            Notice = LastNotice,
            Targets = _strike.OfficialPool(),
            StrikeAvailable = !_strike.Launched,
            StrikeLabel = _strike.Launched ? "отряд уже был заброшен в этом раунде" : string.Empty,
            MercAvailable = !_merc.ContractOpen,
            MercLabel = _merc.ContractOpen ? "действующий контракт ещё не закрыт" : string.Empty,
            CostManeuvers = CostManeuvers,
            CostProvocation = CostProvocation,
            CostSanction = CostSanction,
            CostBlockade = CostBlockade,
            CostCrisis = CostCrisis,
            CostRepublicCrisis = CostRepublicCrisis,
            CostDilemma = CostDilemma,
            CostStrike = CostStrike,
            CostMerc = CostMerc,
        };

        var blockades = state.DefenseMap.Events.Where(e => !e.Resolved && e.Kind == SWDefenseEventKind.Blockade).ToArray();
        state.BlockadeActive = blockades.Length > 0;
        state.BlockadeLabel = blockades.Length == 0 ? string.Empty : $"участков: {blockades.Length}";

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
