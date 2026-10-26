using System.Linq;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Ministries;
using Content.Shared._SW._Senate.Sectors;
using Content.Shared.GameTicking;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Content.Server._SW._Senate.Economy;

namespace Content.Server._SW._Senate.Core;

public sealed class SWMinistryInternalSecuritySystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly SWSenateStateSystem _senate = default!;
    [Dependency] private readonly SWSenateDilemmaSystem _dilemma = default!;
    [Dependency] private readonly SWBureauWeaponsSystem _weapons = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    public const string SecurityMinistryId = "SWMinistryInternalSecurity";

    private const float TickInterval = 1f;
    private const float ThreatStart = 30f;
    private const float UnrestStart = 20f;

    private const int CellCap = 5;
    private const float UpkeepPerHour = 80f;        // per surveilling cell, per game-hour
    private const float SurveillanceSecurity = 0.3f; // sector security bias per game-hour, per covering cell
    private const int RebuildHours = 3;
    private const int RebuildCost = 1200;
    private const int ExpressRebuildCost = 3000;

    private const int MaxActiveOps = 2;
    private const int MaxPlots = 4;
    private const int RaidCost = 800;
    private const float PlotMaturityPerHour = 5f;

    private static readonly SWPlotType[] PlotTypes =
    {
        SWPlotType.Sedition,
        SWPlotType.Assassination,
        SWPlotType.Separatist,
        SWPlotType.Smuggling,
    };

    // Escalation to this posture needs the Chancellor's ratification.
    private const SWSecurityPosture ApprovalThreshold = SWSecurityPosture.Emergency;

    private readonly List<SWCell> _cells = new();
    private int _nextCellId = 1;
    private float _upkeepAccum;

    private readonly List<SWActiveOperation> _activeOps = new();
    private readonly Dictionary<string, double> _cooldowns = new();
    private readonly List<string> _log = new();

    private readonly List<SWPlot> _plots = new();
    private int _nextPlotId = 1;

    private SWSecurityPosture _posture = SWSecurityPosture.Watch;
    private SWSecurityPosture? _pending;
    private float _threat = ThreatStart;
    private float _unrest = UnrestStart;
    private string? _program;
    private float _progress;
    private int _plotsBusted;

    private int _day = -1;
    private float _accum;

    public SWSecurityPosture Posture => _posture;
    public SWSecurityPosture? PendingPosture => _pending;
    public bool EmergencyPending => _pending != null;
    public int Threat => (int) MathF.Round(_threat);
    public int Unrest => (int) MathF.Round(_unrest);
    public string? SelectedProgram => _program;
    public int ProgramProgress => (int) MathF.Round(_progress);
    public int PlotsBusted => _plotsBusted;

    public override void Initialize()
    {
        UpdatesBefore.Add(typeof(SWSenateTimeSystem));
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        SubscribeLocalEvent<SWSenateDayClosedEvent>(ev =>
        {
            if (ev.FinalDay)
                return;
            CompleteRebuilds();
            CompleteOperations();
        });
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _accum = 0f;
        _posture = SWSecurityPosture.Watch;
        _pending = null;
        _threat = ThreatStart;
        _unrest = UnrestStart;
        _program = null;
        _progress = 0f;
        _cells.Clear();
        _nextCellId = 1;
        _upkeepAccum = 0f;
        _activeOps.Clear();
        _cooldowns.Clear();
        _log.Clear();
        _plots.Clear();
        _nextPlotId = 1;
        _plotsBusted = 0;
        _day = -1;
    }

    private void AddLog(string text)
    {
        _log.Insert(0, text);
        if (_log.Count > 6)
            _log.RemoveRange(6, _log.Count - 6);
    }

    public List<string> GetLog() => new(_log);

    public static string PostureName(SWSecurityPosture posture) => posture switch
    {
        SWSecurityPosture.Civil => "Гражданский",
        SWSecurityPosture.Watch => "Наблюдение",
        SWSecurityPosture.Heightened => "Усиленный",
        _ => "Чрезвычайное положение",
    };

    public static Color PostureColor(SWSecurityPosture posture) => posture switch
    {
        SWSecurityPosture.Civil => Color.FromHex("#43D16A"),
        SWSecurityPosture.Watch => Color.FromHex("#5DADE2"),
        SWSecurityPosture.Heightened => Color.FromHex("#E0A23B"),
        _ => Color.FromHex("#E0556B"),
    };

    public static string PlotTypeName(SWPlotType type) => type switch
    {
        SWPlotType.Sedition => "Мятеж",
        SWPlotType.Assassination => "Покушение",
        SWPlotType.Separatist => "Сепаратизм",
        _ => "Контрабанда",
    };

    public static Color PlotTypeColor(SWPlotType type) => type switch
    {
        SWPlotType.Sedition => Color.FromHex("#E0556B"),
        SWPlotType.Assassination => Color.FromHex("#C0392B"),
        SWPlotType.Separatist => Color.FromHex("#E67E22"),
        _ => Color.FromHex("#B7950B"),
    };

    public string ThreatStatus() => _threat switch
    {
        >= 80f => "Критическая",
        >= 55f => "Высокая",
        >= 30f => "Умеренная",
        _ => "Низкая",
    };

    public string UnrestStatus() => _unrest switch
    {
        >= 80f => "Бунты",
        >= 55f => "Волнения",
        >= 30f => "Напряжение",
        _ => "Спокойно",
    };

    public void SetThreat(float value) => _threat = Math.Clamp(value, 0f, 100f);
    public void AddThreat(float delta) => _threat = Math.Clamp(_threat + delta, 0f, 100f);
    public void SetUnrest(float value) => _unrest = Math.Clamp(value, 0f, 100f);
    public void AddUnrest(float delta) => _unrest = Math.Clamp(_unrest + delta, 0f, 100f);

    // (threat per game-hour, unrest per game-hour, sector nudges per game-hour)
    private static (float Threat, float Unrest, (string Stat, float Delta)[] Nudges) Config(SWSecurityPosture posture) => posture switch
    {
        SWSecurityPosture.Civil => (3.0f, -2.5f, new[]
        {
            ("SWGlobalSociety", 0.3f), ("SWGlobalSecurity", -0.25f),
        }),
        SWSecurityPosture.Watch => (1.5f, -0.5f, new[]
        {
            ("SWGlobalSecurity", 0.1f),
        }),
        SWSecurityPosture.Heightened => (-1.5f, 2.0f, new[]
        {
            ("SWGlobalSecurity", 0.3f), ("SWGlobalSociety", -0.2f),
        }),
        _ => (-3.5f, 4.0f, new[]
        {
            ("SWGlobalSecurity", 0.5f), ("SWGlobalSociety", -0.5f), ("SWGlobalEconomy", -0.2f),
        }),
    };

    public void SetPosture(SWSecurityPosture target)
    {
        if (target == _posture)
        {
            _pending = null;
            return;
        }

        var escalating = (byte) target > (byte) _posture;
        if (escalating && target >= ApprovalThreshold && !_senate.OfficesVacant)
        {
            _pending = target;
            return;
        }

        _pending = null;
        ChangePosture(target);
    }

    public bool ApproveEmergency()
    {
        if (_pending is not { } target)
            return false;

        _pending = null;
        ChangePosture(target);
        return true;
    }

    public bool RejectEmergency()
    {
        if (_pending == null)
            return false;

        _pending = null;
        return true;
    }

    private void ChangePosture(SWSecurityPosture posture)
    {
        if (posture == _posture)
            return;

        _posture = posture;
        RaiseLocalEvent(new SWSenateCustomAnnouncementEvent("Министерство ВБ",
            $"Режим безопасности: {PostureName(posture)}.",
            SWSenateAnnouncementKind.Security));
    }

    public void SelectProgram(string programId)
    {
        if (!_proto.HasIndex<SWSecurityProgramPrototype>(programId))
            return;

        _program = programId;
    }

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

        if (_pending is { } pending && _senate.OfficesVacant)
        {
            _pending = null;
            ChangePosture(pending);
        }

        if (_time.Day != _day)
        {
            if (_day != -1)
                ApplyProgram();
            _day = _time.Day;
            _program = null;
            _progress = 0f;
        }

        if (!_time.DayActive)
            return;

        CompleteOperations();

        var gameHours = dt * (1f / SWSenateTimeSystem.RealSecondsPerGameHour);
        var cfg = Config(_posture);

        EnsureCells();
        PruneCells();
        CompleteRebuilds();
        var cellService = TickUpkeep(_cells.Count(c => c.State == SWCellState.Sector), gameHours);


        // Buildup speed is tied to the Republic: a weak state lets threats and unrest spiral several
        // times faster, a strong one keeps them in check (direction-aware).
        var threatScale = _senate.StatPressureScale("SWGlobalSecurity", cfg.Threat);
        var unrestScale = _senate.StatPressureScale("SWGlobalSociety", cfg.Unrest);
        _threat = Math.Clamp(_threat + cfg.Threat * threatScale * gameHours, 0f, 100f);
        _unrest = Math.Clamp(_unrest + cfg.Unrest * unrestScale * gameHours, 0f, 100f);

        // Discontent feeds on low rights and morale; order represses it.
        UnrestFeedback(gameHours);

        if (_threat >= 70f)
            NudgeSectors("SWGlobalSecurity", -0.3f * gameHours);
        if (_unrest >= 70f)
        {
            NudgeSectors("SWGlobalSociety", -0.3f * gameHours);
            RollRiot(gameHours);
        }

        foreach (var (stat, delta) in cfg.Nudges)
            NudgeSectors(stat, delta * gameHours);

        foreach (var cell in _cells)
        {
            if (cell.State != SWCellState.Sector || cell.SectorId == null)
                continue;

            var scale = (cell.Burned ? 0.5f : 1f) * cellService;
            _senate.AddSectorBias(cell.SectorId, "SWGlobalSecurity", SurveillanceSecurity * scale * gameHours);
        }

        TickPlots(gameHours);

        if (_program != null && _progress < 100f)
        {
            var perHour = 100f / (SWSenateTimeSystem.EndHour - SWSenateTimeSystem.StartHour);
            _progress = Math.Clamp(_progress + perHour * gameHours, 0f, 100f);
        }
    }

    private void UnrestFeedback(float gameHours)
    {
        if (!_senate.SubStats.TryGetValue("SWStatRights", out var rights))
            rights = 50f;
        if (!_senate.SubStats.TryGetValue("SWStatMorale", out var morale))
            morale = 50f;

        // Below ~40 average, freedoms/morale starvation stokes unrest; above it, soothes.
        var pressure = (40f - (rights + morale) / 2f) * 0.05f;
        _unrest = Math.Clamp(_unrest + pressure * gameHours, 0f, 100f);
    }

    private void EnsureCells()
    {
        var target = Math.Clamp(2 + _senate.SectorHolders.Count / 6, 2, CellCap);
        while (_cells.Count < target)
            _cells.Add(new SWCell { Id = _nextCellId++, State = SWCellState.Reserve });
    }

    private void PruneCells()
    {
        foreach (var cell in _cells)
        {
            if (cell.State == SWCellState.Sector && (cell.SectorId == null || !_senate.IsClaimed(cell.SectorId)))
            {
                cell.State = SWCellState.Reserve;
                cell.SectorId = null;
            }
        }
    }

    private float TickUpkeep(int deployed, float gameHours)
    {
        var paid = _dilemma.FundWork(SecurityMinistryId, deployed * UpkeepPerHour * gameHours, ref _upkeepAccum);
        if (paid > 0f || deployed == 0)
            return paid;
        foreach (var cell in _cells)
        {
            if (cell.State != SWCellState.Sector)
                continue;
            cell.State = SWCellState.Reserve;
            cell.SectorId = null;
        }
        AddLog("Агентура отозвана: нет средств на содержание сети.");
        return 0f;
    }

    private void CompleteRebuilds()
    {
        foreach (var cell in _cells)
        {
            if (cell.State == SWCellState.Rebuilding && _time.ActiveGameHours >= cell.ReadyHour)
            {
                cell.Burned = false;
                cell.ReadyHour = 0;
                cell.State = SWCellState.Reserve;
                cell.SectorId = null;
            }
        }
    }

    public bool DeployCell(int cellId, string target)
    {
        var cell = _cells.FirstOrDefault(c => c.Id == cellId);
        if (cell == null || cell.State == SWCellState.Rebuilding)
            return false;

        if (target == "express")
        {
            if (!cell.Burned || !_dilemma.SpendMinistry(SecurityMinistryId, ExpressRebuildCost))
                return false;
            cell.Burned = false;
            return true;
        }

        if (target == "rebuild")
        {
            if (!cell.Burned || !_dilemma.SpendMinistry(SecurityMinistryId, RebuildCost))
                return false;
            cell.State = SWCellState.Rebuilding;
            cell.SectorId = null;
            cell.ReadyHour = _time.ActiveGameHours + RebuildHours;
            return true;
        }

        switch (target)
        {
            case "":
            case "reserve":
                cell.State = SWCellState.Reserve;
                cell.SectorId = null;
                return true;
            default:
                if (!_senate.IsClaimed(target))
                    return false;
                cell.State = SWCellState.Sector;
                cell.SectorId = target;
                RevealPlots(target);
                return true;
        }
    }

    public int CellUpkeep => (int) MathF.Round(_cells.Count(c => c.State == SWCellState.Sector) * UpkeepPerHour);

    private int CellsOn(string sectorId) =>
        _cells.Count(c => c.State == SWCellState.Sector && c.SectorId == sectorId && !c.Burned);

    public List<SWCellEntry> BuildCells()
    {
        EnsureCells();
        var list = new List<SWCellEntry>();
        foreach (var cell in _cells)
        {
            var label = cell.State switch
            {
                SWCellState.Rebuilding => "Восстановление",
                SWCellState.Sector => cell.SectorId != null && _proto.TryIndex<SWSectorPrototype>(cell.SectorId, out var sp) ? sp.Name : "Сектор",
                _ => "Резерв",
            };

            list.Add(new SWCellEntry
            {
                Id = cell.Id,
                State = cell.State,
                LocationLabel = label,
                SectorId = cell.SectorId,
                Burned = cell.Burned,
                RebuildLeft = cell.State == SWCellState.Rebuilding ? Math.Max(0, cell.ReadyHour - _time.ActiveGameHours) : 0,
            });
        }

        return list;
    }

    public List<SWCellSectorEntry> BuildCellSectors()
    {
        var list = new List<SWCellSectorEntry>();
        foreach (var (sectorId, _) in _senate.SectorHolders)
        {
            if (!_proto.TryIndex<SWSectorPrototype>(sectorId, out var proto))
                continue;

            var order = _senate.SectorGlobals.TryGetValue(sectorId, out var globals)
                && globals.TryGetValue("SWGlobalSecurity", out var s)
                    ? (int) MathF.Round(s)
                    : 50;

            var entry = new SWCellSectorEntry
            {
                Id = sectorId,
                Name = proto.Name,
                Color = proto.Color,
                Order = order,
                Cells = _cells.Count(c => c.State == SWCellState.Sector && c.SectorId == sectorId),
                Arms = (int) MathF.Round(_weapons.ArmsLevel(sectorId)),
            };

            var plot = _plots.FirstOrDefault(p => p.SectorId == sectorId);
            if (plot != null)
            {
                entry.HasPlot = true;
                entry.PlotRevealed = plot.Revealed;
                if (plot.Revealed)
                {
                    entry.PlotLabel = $"{PlotTypeName(plot.Type)} {(int) plot.Maturity}%";
                    entry.PlotColor = PlotTypeColor(plot.Type);
                }
                else if (plot.Maturity >= 75f)
                {
                    entry.PlotLabel = "тревожные сигналы";
                    entry.PlotColor = Color.FromHex("#E0A23B");
                }
                else
                {
                    entry.HasPlot = false;
                }
            }

            list.Add(entry);
        }

        list.Sort((a, b) => a.Order.CompareTo(b.Order));
        return list;
    }

    private int RaidChance(SWPlot plot)
    {
        var chance = 35 + CellsOn(plot.SectorId) * 25;
        chance += _posture switch
        {
            SWSecurityPosture.Heightened => 10,
            SWSecurityPosture.Emergency => 20,
            _ => 0,
        };
        chance -= (int) (_weapons.ArmsLevel(plot.SectorId) / 5f);
        chance += _weapons.SecurityDroids * 3;
        return Math.Clamp(chance, 5, 95);
    }

    public void DampPlots(string sectorId, float delta)
    {
        foreach (var plot in _plots)
        {
            if (plot.SectorId == sectorId)
                plot.Maturity = Math.Clamp(plot.Maturity + delta, 0f, 100f);
        }
    }

    public List<SWPlotEntry> BuildPlots()
    {
        var list = new List<SWPlotEntry>();
        var budget = _dilemma.GetMinistryBudget(SecurityMinistryId);

        foreach (var plot in _plots)
        {
            if (!plot.Revealed && plot.Maturity < 75f)
                continue;

            var name = _proto.TryIndex<SWSectorPrototype>(plot.SectorId, out var sp) ? sp.Name : plot.SectorId;
            list.Add(new SWPlotEntry
            {
                Id = plot.Id,
                SectorId = plot.SectorId,
                SectorName = name,
                TypeLabel = plot.Revealed ? PlotTypeName(plot.Type) : "Неизвестно",
                TypeColor = plot.Revealed ? PlotTypeColor(plot.Type) : Color.FromHex("#888888"),
                Maturity = (int) plot.Maturity,
                Revealed = plot.Revealed,
                Cells = CellsOn(plot.SectorId),
                SuccessChance = RaidChance(plot),
                CanRaid = plot.Revealed && budget >= RaidCost,
                ThreatLabel = plot.Maturity >= 80f ? "созревает" : plot.Maturity >= 50f ? "развивается" : "в зачатке",
                Arms = (int) MathF.Round(_weapons.ArmsLevel(plot.SectorId)),
            });
        }

        list.Sort((a, b) => b.Maturity.CompareTo(a.Maturity));
        return list;
    }

    public bool RaidPlot(int plotId)
    {
        var plot = _plots.FirstOrDefault(p => p.Id == plotId);
        if (plot == null || !plot.Revealed)
            return false;
        if (!_dilemma.SpendMinistry(SecurityMinistryId, RaidCost))
            return false;

        var name = _proto.TryIndex<SWSectorPrototype>(plot.SectorId, out var sp) ? sp.Name : plot.SectorId;

        if (_random.Prob(RaidChance(plot) / 100f))
        {
            _plots.Remove(plot);
            _plotsBusted++;
            AddThreat(-4f);
            AddUnrest(3f);
            if (_senate.IsClaimed(plot.SectorId))
                _senate.AddSectorBias(plot.SectorId, "SWGlobalSecurity", 4f);
            AddLog($"Облава в {name}: заговор '{PlotTypeName(plot.Type)}' раскрыт.");
        }
        else
        {
            plot.Maturity = Math.Clamp(plot.Maturity + 20f, 0f, 100f);
            AddUnrest(5f);
            BurnCell(plot.SectorId);
            AddLog($"Облава в {name} провалена: заговор ушёл в тень.");
        }

        return true;
    }

    private void BurnCell(string sectorId)
    {
        var cell = _cells.FirstOrDefault(c => c.State == SWCellState.Sector && c.SectorId == sectorId && !c.Burned)
                   ?? _cells.FirstOrDefault(c => c.State == SWCellState.Sector && !c.Burned);
        if (cell == null)
            return;

        cell.Burned = true;
        AddLog($"Ячейка {cell.Id} раскрыта - нужна перестройка прикрытия.");
    }

    private void RevealPlots(string sectorId)
    {
        foreach (var plot in _plots)
            if (plot.SectorId == sectorId)
                plot.Revealed = true;
    }

    private void TickPlots(float gameHours)
    {
        var claimed = _senate.SectorHolders.Keys.ToList();

        // Seed new plots; rate climbs with threat and thins with order.
        if (claimed.Count > 0 && _plots.Count < MaxPlots)
        {
            var spawn = (0.06f + _threat / 100f * 0.20f) * gameHours;
            if (_random.Prob(Math.Clamp(spawn, 0f, 1f)))
            {
                var open = claimed.Where(s => _plots.All(p => p.SectorId != s)).ToList();
                if (open.Count > 0)
                {
                    var sectorId = _random.Pick(open);
                    var plot = new SWPlot
                    {
                        Id = _nextPlotId++,
                        SectorId = sectorId,
                        Type = _random.Pick(PlotTypes),
                        Maturity = 0f,
                        Revealed = CellsOn(sectorId) > 0,
                    };
                    _plots.Add(plot);
                }
            }
        }

        for (var i = _plots.Count - 1; i >= 0; i--)
        {
            var plot = _plots[i];

            if (!_senate.IsClaimed(plot.SectorId))
            {
                _plots.RemoveAt(i);
                continue;
            }

            if (CellsOn(plot.SectorId) > 0)
                plot.Revealed = true;

            plot.Maturity += PlotMaturityPerHour * (0.7f + _threat / 200f) * (1f + _weapons.ArmsLevel(plot.SectorId) / 150f) * gameHours;
            if (plot.Maturity >= 100f)
            {
                MaturePlot(plot);
                _plots.RemoveAt(i);
            }
        }
    }

    private void MaturePlot(SWPlot plot)
    {
        var name = _proto.TryIndex<SWSectorPrototype>(plot.SectorId, out var sp) ? sp.Name : plot.SectorId;
        AddThreat(8f);
        AddUnrest(6f);

        var claimed = _senate.IsClaimed(plot.SectorId);
        void Sector(string global, float delta)
        {
            if (claimed)
                _senate.AddSectorBias(plot.SectorId, global, delta);
        }

        switch (plot.Type)
        {
            case SWPlotType.Sedition:
                Sector("SWGlobalSecurity", -6f);
                Sector("SWGlobalSociety", -4f);
                break;
            case SWPlotType.Assassination:
                Sector("SWGlobalSecurity", -4f);
                _senate.AddPolicy("SWStatMorale", -3f);
                break;
            case SWPlotType.Separatist:
                Sector("SWGlobalSecurity", -5f);
                Sector("SWGlobalSociety", -3f);
                _senate.AddPolicy("SWStatNeutral", -3f);
                break;
            default:
                Sector("SWGlobalEconomy", -4f);
                Sector("SWGlobalSecurity", -3f);
                break;
        }

        AddLog($"Заговор '{PlotTypeName(plot.Type)}' в {name} реализован.");
    }

    private void RollRiot(float gameHours)
    {
        if (!_random.Prob(0.12f * gameHours))
            return;

        _senate.AddPolicy("SWStatMorale", -2f);
        _senate.AddPolicy("SWStatLiving", -1.5f);
        AddLog("Массовые беспорядки: мораль и уровень жизни просели.");
    }

    private void ApplyProgram()
    {
        if (_program == null || !_proto.TryIndex<SWSecurityProgramPrototype>(_program, out var proto))
            return;

        var scale = _progress / 100f;
        if (scale <= 0f)
            return;

        foreach (var (sub, delta) in proto.PolicyEffects)
            _senate.AddPolicy(sub, delta * scale);

        if (proto.ThreatDelta != 0f)
            AddThreat(proto.ThreatDelta * scale);
        if (proto.UnrestDelta != 0f)
            AddUnrest(proto.UnrestDelta * scale);
    }

    private void NudgeSectors(string globalId, float delta)
    {
        if (delta == 0f)
            return;

        foreach (var (sectorId, _) in _senate.SectorHolders)
            _senate.AddSectorBias(sectorId, globalId, delta);
    }

    // Security ministry requests are modulated by surveillance coverage and posture.
    public (float Cost, float Effect) GetRequestModifier(string? sectorId)
    {
        var covered = sectorId != null && CellsOn(sectorId) > 0;

        var cost = 1f;
        var effect = 1f;

        if (covered)
        {
            cost *= 0.7f;
            effect *= 1.2f;
        }

        if (_posture >= SWSecurityPosture.Emergency)
            effect *= 1.15f;

        if (_posture == SWSecurityPosture.Civil && !covered)
        {
            cost *= 1.2f;
            effect *= 0.85f;
        }

        return (cost, effect);
    }

    // Resolving a sector request locally disrupts whatever was brewing there.
    public void OnSectorEngagement(string? sectorId)
    {
        if (sectorId == null)
            return;

        var plot = _plots.FirstOrDefault(p => p.SectorId == sectorId);
        if (plot == null)
            return;

        plot.Revealed = true;
        plot.Maturity = Math.Clamp(plot.Maturity - 15f, 0f, 100f);
    }

    public bool LaunchOperation(string opId, string target)
    {
        if (!_proto.TryIndex<SWSecurityOperationPrototype>(opId, out var op))
            return false;
        if (_posture < op.RequirePosture)
            return false;
        if (_cooldowns.TryGetValue(opId, out var ready) && _time.ActiveGameHours < ready)
            return false;

        string? sectorId = null;
        if (op.Target == SWOpTarget.Sector)
        {
            if (!_senate.IsClaimed(target))
                return false;
            sectorId = target;
        }

        if (op.Duration > 0 && _activeOps.Count >= MaxActiveOps)
            return false;

        if (_dilemma.GetMinistryAdmin(SecurityMinistryId) < op.AdminCost
            || _dilemma.GetMinistryBudget(SecurityMinistryId) < op.Cost)
            return false;

        _dilemma.SpendMinistryAdmin(SecurityMinistryId, op.AdminCost);
        _dilemma.SpendMinistry(SecurityMinistryId, op.Cost);
        _cooldowns[opId] = _time.ActiveGameHours + op.Cooldown;

        if (op.Duration <= 0)
            ApplyOp(op, sectorId);
        else
            _activeOps.Add(new SWActiveOperation { OpId = opId, SectorId = sectorId, CompleteHour = _time.ActiveGameHours + op.Duration });

        return true;
    }

    private void ApplyOp(SWSecurityOperationPrototype op, string? sectorId)
    {
        if (op.ThreatDelta != 0f)
            AddThreat(op.ThreatDelta);
        if (op.UnrestDelta != 0f)
            AddUnrest(op.UnrestDelta);

        foreach (var (sub, delta) in op.PolicyEffects)
            _senate.AddPolicy(sub, delta);

        if (sectorId != null && _senate.IsClaimed(sectorId))
        {
            foreach (var (global, delta) in op.SectorEffects)
                _senate.AddSectorBias(sectorId, global, delta);
        }
    }

    private void CompleteOperations()
    {
        for (var i = _activeOps.Count - 1; i >= 0; i--)
        {
            var active = _activeOps[i];
            if (_time.ActiveGameHours < active.CompleteHour)
                continue;

            if (_proto.TryIndex<SWSecurityOperationPrototype>(active.OpId, out var op))
                ApplyOp(op, active.SectorId);

            _activeOps.RemoveAt(i);
        }
    }

    public int ActiveOpSlots => MaxActiveOps;
    public int ActiveOpUsed => _activeOps.Count;

    public List<SWDefenseOperationEntry> BuildOperations()
    {
        var admin = _dilemma.GetMinistryAdmin(SecurityMinistryId);
        var budget = _dilemma.GetMinistryBudget(SecurityMinistryId);
        var slotsFree = _activeOps.Count < MaxActiveOps;

        var list = new List<SWDefenseOperationEntry>();
        foreach (var op in _proto.EnumeratePrototypes<SWSecurityOperationPrototype>().OrderBy(o => o.Order))
        {
            var entry = new SWDefenseOperationEntry
            {
                Id = op.ID,
                Name = op.Name,
                Description = op.Description,
                Cost = op.Cost,
                AdminCost = op.AdminCost,
                Duration = op.Duration,
                Target = op.Target,
                Effects = OpEffects(op),
            };

            if (_cooldowns.TryGetValue(op.ID, out var ready) && _time.ActiveGameHours < ready)
                entry.StatusLabel = $"готовность через {SWSenateDuration.FormatHours(ready - _time.ActiveGameHours)}";
            else if (_posture < op.RequirePosture)
                entry.StatusLabel = $"нужен режим: {PostureName(op.RequirePosture)}";
            else if (op.Duration > 0 && !slotsFree)
                entry.StatusLabel = "нет свободных слотов";
            else if (admin < op.AdminCost)
                entry.StatusLabel = "нет админ-ресурса";
            else if (budget < op.Cost)
                entry.StatusLabel = "недостаточно бюджета";
            else
                entry.Available = true;

            list.Add(entry);
        }

        return list;
    }

    public List<SWActiveOpEntry> BuildActiveOps()
    {
        var list = new List<SWActiveOpEntry>();
        foreach (var active in _activeOps)
        {
            var name = _proto.TryIndex<SWSecurityOperationPrototype>(active.OpId, out var op) ? op.Name : active.OpId;
            var detail = active.SectorId != null && _proto.TryIndex<SWSectorPrototype>(active.SectorId, out var sp) ? sp.Name : "-";
            list.Add(new SWActiveOpEntry { Name = name, Detail = detail, HoursLeft = Math.Max(0, active.CompleteHour - _time.ActiveGameHours) });
        }

        return list;
    }

    private List<SWBillEffectEntry> OpEffects(SWSecurityOperationPrototype op)
    {
        var list = PolicyEffectEntries(op.PolicyEffects);
        foreach (var (global, delta) in op.SectorEffects)
        {
            var name = global.Id;
            var color = Color.White;
            if (_proto.TryIndex<SWGlobalStatPrototype>(global, out var gp))
            {
                name = gp.ShortName;
                color = gp.Color;
            }

            list.Add(new SWBillEffectEntry { StatName = name, Color = color, Delta = delta });
        }

        if (op.ThreatDelta != 0f)
            list.Add(new SWBillEffectEntry { StatName = "Угроза", Color = Color.FromHex("#E0556B"), Delta = op.ThreatDelta });
        if (op.UnrestDelta != 0f)
            list.Add(new SWBillEffectEntry { StatName = "Накал", Color = Color.FromHex("#E0A23B"), Delta = op.UnrestDelta });

        return list;
    }

    public List<SWBillEffectEntry> BuildPostureEffects(SWSecurityPosture posture)
    {
        var cfg = Config(posture);
        var list = new List<SWBillEffectEntry>();
        foreach (var (stat, delta) in cfg.Nudges)
        {
            var name = stat;
            var color = Color.White;
            if (_proto.TryIndex<SWGlobalStatPrototype>(stat, out var global))
            {
                name = global.ShortName;
                color = global.Color;
            }

            list.Add(new SWBillEffectEntry { StatName = name, Color = color, Delta = delta });
        }

        list.Add(new SWBillEffectEntry { StatName = "Угроза", Color = Color.FromHex("#E0556B"), Delta = cfg.Threat });
        list.Add(new SWBillEffectEntry { StatName = "Накал", Color = Color.FromHex("#E0A23B"), Delta = cfg.Unrest });
        return list;
    }

    public List<SWDefenseProgramEntry> BuildPrograms()
    {
        var list = new List<SWDefenseProgramEntry>();
        foreach (var proto in _proto.EnumeratePrototypes<SWSecurityProgramPrototype>().OrderBy(p => p.Order))
        {
            var entry = new SWDefenseProgramEntry
            {
                Id = proto.ID,
                Name = proto.Name,
                Description = proto.Description,
                Selected = proto.ID == _program,
                Effects = PolicyEffectEntries(proto.PolicyEffects),
            };

            if (proto.ThreatDelta != 0f)
                entry.Effects.Add(new SWBillEffectEntry { StatName = "Угроза", Color = Color.FromHex("#E0556B"), Delta = proto.ThreatDelta });
            if (proto.UnrestDelta != 0f)
                entry.Effects.Add(new SWBillEffectEntry { StatName = "Накал", Color = Color.FromHex("#E0A23B"), Delta = proto.UnrestDelta });

            list.Add(entry);
        }

        return list;
    }

    private List<SWBillEffectEntry> PolicyEffectEntries(Dictionary<ProtoId<SWStatPrototype>, float> effects)
    {
        var list = new List<SWBillEffectEntry>();
        foreach (var sub in _proto.EnumeratePrototypes<SWStatPrototype>().OrderBy(s => s.Order))
        {
            if (!effects.TryGetValue(sub.ID, out var delta))
                continue;

            var color = _proto.TryIndex(sub.Global, out var global) ? global.Color : Color.White;
            list.Add(new SWBillEffectEntry { StatName = sub.ShortName, Color = color, Delta = delta });
        }

        return list;
    }
}

public sealed class SWCell
{
    public int Id;
    public SWCellState State;
    public string? SectorId;
    public bool Burned;
    public double ReadyHour;
}

public sealed class SWPlot
{
    public int Id;
    public string SectorId = string.Empty;
    public SWPlotType Type;
    public float Maturity;
    public bool Revealed;
}
