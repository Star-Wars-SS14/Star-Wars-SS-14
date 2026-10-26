using System.Linq;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Sectors;
using Content.Shared.GameTicking;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._SW._Senate.Core;

// Republic-wide corruption: a creeping level on every sector and on every office-holder, plus a private
// stash each player can skim into. The Justice ministry screens and prosecutes; seized funds return to the
// treasury. Players are keyed by holder name (as the rest of the Senate systems do).
public sealed class SWCorruptionSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly SWSenateStateSystem _senate = default!;
    [Dependency] private readonly SWMinistrySystem _ministry = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    private const float TickInterval = 1f;
    private const float SectorDrift = 0.06f;     // baseline entropy per game-hour
    private const float CostFactor = 0.6f;        // sector cost surcharge at 100 corruption
    private const float ScandalBase = 0.02f;

    private readonly Dictionary<string, float> _sector = new();
    private readonly Dictionary<string, float> _person = new();
    private readonly Dictionary<string, int> _account = new();
    private readonly HashSet<string> _revealed = new();   // keys ("s:" + sector / "p:" + name) screened by Justice
    private readonly List<string> _log = new();

    private int _day = -1;
    private float _accum;

    public override void Initialize()
    {
        UpdatesBefore.Add(typeof(SWSenateTimeSystem));
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _accum = 0f;
        _sector.Clear();
        _person.Clear();
        _account.Clear();
        _revealed.Clear();
        _log.Clear();
        _day = -1;
    }

    private void AddLog(string text)
    {
        _log.Insert(0, text);
        if (_log.Count > 8)
            _log.RemoveRange(8, _log.Count - 8);
    }

    public List<string> GetLog() => new(_log);

    public float SectorCorruption(string sectorId) => _sector.TryGetValue(sectorId, out var v) ? v : 0f;
    public int SectorCorruptionInt(string sectorId) => (int) MathF.Round(SectorCorruption(sectorId));
    public IReadOnlyDictionary<string, float> Persons => _person;
    public float PersonCorruption(string name) => _person.TryGetValue(name, out var v) ? v : 0f;
    public int PersonCorruptionInt(string name) => (int) MathF.Round(PersonCorruption(name));
    public int Account(string name) => _account.TryGetValue(name, out var v) ? v : 0;

    // Sector corruption inflates the cost of everything that draws money there.
    public float CostMultiplier(string? sectorId) =>
        sectorId == null ? 1f : 1f + SectorCorruption(sectorId) / 100f * CostFactor;

    public int ProjectCost(string sectorId, int baseCost) =>
        (int) MathF.Ceiling(Math.Max(0, baseCost) * CostMultiplier(sectorId));

    public int GlobalIndex()
    {
        var vals = _sector.Values.Concat(_person.Values).ToList();
        return vals.Count > 0 ? (int) MathF.Round(vals.Average()) : 0;
    }

    public string GlobalStatus() => GlobalIndex() switch
    {
        >= 70 => "Системная",
        >= 45 => "Высокая",
        >= 25 => "Заметная",
        _ => "Низкая",
    };

    // A player skims from a decision into their private account; the sector and the official both rot.
    // Deliberately NOT logged - a bribe is secret. Justice learns of it only via screening, prosecution
    // or a public scandal; the briber alone gets a private notice in the dilemma system.
    public void TakeBribe(string player, string? sectorId, int amount)
    {
        _account[player] = Account(player) + amount;
        _person[player] = Math.Clamp(PersonCorruption(player) + 12f, 0f, 100f);
        if (sectorId != null)
            _sector[sectorId] = Math.Clamp(SectorCorruption(sectorId) + 8f, 0f, 100f);
    }

    public void AddSectorCorruption(string sectorId, float delta) =>
        _sector[sectorId] = Math.Clamp(SectorCorruption(sectorId) + delta, 0f, 100f);

    public void AddPersonCorruption(string name, float delta) =>
        _person[name] = Math.Clamp(PersonCorruption(name) + delta, 0f, 100f);

    // Clean income (salary, etc) - credits the same personal balance bribes use, but never touches
    // the corruption index, since the account is just a wallet and corruption is a separate reputation.
    public void AddAccount(string name, int amount)
    {
        if (amount <= 0)
            return;

        _account[name] = Account(name) + amount;
    }

    public bool SpendAccount(string name, int amount)
    {
        if (amount <= 0 || Account(name) < amount)
            return false;

        _account[name] = Account(name) - amount;
        return true;
    }

    // --- Justice tools ---

    public bool IsRevealed(bool sector, string key) => _revealed.Contains((sector ? "s:" : "p:") + key);

    public void Screen(bool sector, string key) => _revealed.Add((sector ? "s:" : "p:") + key);

    // Prosecute a target. Real guilt = the (possibly hidden) corruption level. Returns a result label.
    public string Prosecute(bool sector, string key)
    {
        if (sector)
        {
            var corr = SectorCorruption(key);
            _revealed.Add("s:" + key);
            var name = _proto.TryIndex<SWSectorPrototype>(key, out var sp) ? sp.Name : key;
            if (corr >= 45f)
            {
                _sector[key] = Math.Clamp(corr - 50f, 0f, 100f);
                _senate.AddSectorBias(key, "SWGlobalSecurity", 4f);
                _senate.AddPolicy("SWStatTreasury", MathF.Min(corr / 12f, 8f));
                AddLog($"Сектор {name}: коррупционная сеть разоблачена и вычищена.");
                return $"Коррупция в {name} вскрыта и подавлена.";
            }

            _senate.AddSectorBias(key, "SWGlobalSociety", -3f);
            AddLog($"Сектор {name}: расследование не нашло коррупции - недовольство.");
            return $"В {name} коррупции не найдено - удар по доверию.";
        }

        var pc = PersonCorruption(key);
        _revealed.Add("p:" + key);
        if (pc >= 45f)
        {
            var seized = Account(key);
            _account[key] = 0;
            _person[key] = 0f;
            if (seized > 0)
                _senate.AddPolicy("SWStatTreasury", MathF.Min(seized / 500f, 15f));
            _senate.AddPolicy("SWStatMorale", 2f);
            AddLog($"{key} осуждён за коррупцию: изъято {seized} кр в казну.");
            return $"{key} осуждён, изъято {seized} кр в казну.";
        }

        _senate.AddPolicy("SWStatRights", -3f);
        _senate.AddPolicy("SWStatMorale", -2f);
        AddLog($"{key}: обвинение в коррупции не подтвердилось.");
        return $"{key} оправдан - ложное обвинение бьёт по правам.";
    }

    // --- Admin (swcorrupt) ---

    public void AdminSetSector(string sectorId, float value) => _sector[sectorId] = Math.Clamp(value, 0f, 100f);
    public void AdminSetPerson(string name, float value) => _person[name] = Math.Clamp(value, 0f, 100f);
    public void AdminSetAccount(string name, int value) => _account[name] = Math.Max(0, value);
    public string AdminStatus() => $"Индекс коррупции {GlobalIndex()}/100 ({GlobalStatus()}). Секторов: {_sector.Count}, лиц: {_person.Count}.";

    public override void Update(float frameTime)
    {
        frameTime = _time.ActiveSeconds(frameTime);
        if (frameTime <= 0f)
            return;
        if (!_time.ModeActive)
            return;

        _accum += frameTime;
        if (_accum < TickInterval)
            return;

        var dt = _accum;
        _accum = 0f;

        if (_time.Day != _day)
            _day = _time.Day;

        if (!_time.DayActive)
            return;

        var gameHours = dt * (1f / SWSenateTimeSystem.RealSecondsPerGameHour);

        // Entropy: corruption creeps up in occupied sectors over time.
        foreach (var (sectorId, _) in _senate.SectorHolders)
            _sector[sectorId] = Math.Clamp(SectorCorruption(sectorId) + SectorDrift * gameHours, 0f, 100f);

        RollScandals(gameHours);
    }

    private void RollScandals(float gameHours)
    {
        foreach (var (name, corr) in _person.ToList())
        {
            if (corr < 60f)
                continue;

            var chance = (corr - 60f) / 40f * ScandalBase * gameHours;
            if (!_random.Prob(Math.Clamp(chance, 0f, 1f)))
                continue;

            _revealed.Add("p:" + name);
            var fine = Account(name) / 4;
            _account[name] = Account(name) - fine;
            _person[name] = Math.Clamp(corr - 15f, 0f, 100f);
            _senate.AddPolicy("SWStatMorale", -3f);
            _senate.AddPolicy("SWStatRights", -1.5f);
            RaiseLocalEvent(new SWSenateCustomAnnouncementEvent("Скандал",
                $"Коррупционный скандал: {name} в центре разоблачения.",
                SWSenateAnnouncementKind.Scandal));
            AddLog($"Скандал: {name} разоблачён, штраф {fine} кр.");
        }
    }
}
