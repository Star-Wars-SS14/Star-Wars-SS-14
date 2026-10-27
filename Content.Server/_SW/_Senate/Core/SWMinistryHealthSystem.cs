using System.Linq;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Ministries;
using Content.Shared._SW._Senate.Sectors;
using Content.Shared._SW._Senate.Economy;
using Content.Shared.GameTicking;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Content.Server._SW._Senate.Economy;
namespace Content.Server._SW._Senate.Core;
public sealed partial class SWMinistryHealthSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly SWSenateStateSystem _senate = default!;
    [Dependency] private readonly SWSenateDilemmaSystem _dilemma = default!;
    [Dependency] private readonly SWMinistrySystem _ministry = default!;
    [Dependency] private readonly SWRepublicEconomySystem _economy = default!;
    [Dependency] private readonly SWSenateProgramSystem _programs = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SWBureauTransitSystem _transit = default!;
    public const string HealthMinistryId = "SWMinistryHealth";
    public const int HospitalMaxLevel = 3;
    private const int AidCost = 750;
    private static readonly string[] PathogenNames = { "Чума Ракгулов", "Синяя Тень", "Лихорадка Кашиика", "Серая оспа", "Споровый бич" };
    private sealed class World(SWGalacticMapWorld map, double population)
    {
        public readonly SWGalacticMapWorld Map = map;
        public readonly double Population = population;
        public float Infection, Immunity, Trend, Reserve, PendingImmunity, IncomingInfectionPerHour;
    }
    private readonly Dictionary<string, World> _worlds = new();
    private SWTransitLinkPrototype[] _links = Array.Empty<SWTransitLinkPrototype>();
    private readonly List<string> _log = new();
    private Content.Server._SW._Senate.Economy.Simulation.EconomySnapshot? _careSnapshot;
    private readonly Dictionary<string, float> _careSupplied = new();
    private bool _active;
    private string _name = string.Empty;
    private int _strain, _episode;
    private float _virulence = 1, _lethality = 1;
    private string? _program;
    private float _progress, _accum;
    private int _day = -1, _hospitalsBuilt;
    private readonly HashSet<string> _countedClinics = new(), _preventedWorlds = new();
    private bool CanManage => _time.ModeActive && _time.DayActive && MinisterActive;
    public bool MinisterActive => _ministry.IsHolderActive(HealthMinistryId);
    public int HospitalsBuilt => _hospitalsBuilt;
    public int HospitalServicePercent => (int) MathF.Round(Weighted(Care) * 100);
    public bool PathogenActive => _active;
    public string PathogenName => _name;
    public int PathogenStrain => _strain;
    public int Virulence => (int) MathF.Round(_virulence * 100);
    public int Immunity => (int) MathF.Round(Weighted(w => w.Immunity));
    public string? SelectedProgram => _program;
    public int ProgramProgress => (int) MathF.Round(_progress);
    public override void Initialize()
    {
        UpdatesBefore.Add(typeof(SWSenateTimeSystem));
        UpdatesAfter.Add(typeof(SWRepublicEconomySystem));
        var scenario = _proto.Index<SWRepublicEconomyScenarioPrototype>(SWRepublicEconomyFactory.ScenarioId);
        foreach (var map in _proto.Index(scenario.Map).Worlds)
            if (map.Sector != null && _proto.TryIndex<SWPlanetEconomyPrototype>(map.Id, out var economy) && economy.ActiveEconomy)
                _worlds[map.Id] = new World(map, Math.Max(.001, economy.PopulationUnits));
        _links = _proto.EnumeratePrototypes<SWTransitLinkPrototype>().Where(l => l.Accessible && _worlds.ContainsKey(l.From) && _worlds.ContainsKey(l.To)).ToArray();
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }
    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        foreach (var w in _worlds.Values) w.Infection = w.Immunity = w.Trend = w.Reserve = w.PendingImmunity = w.IncomingInfectionPerHour = 0;
        _active = false; _name = ""; _strain = _episode = 0; _virulence = _lethality = 1;
        _program = null; _progress = _accum = 0; _day = -1; _hospitalsBuilt = 0;
        _careSnapshot = null; _careSupplied.Clear();
        _log.Clear(); _countedClinics.Clear(); _preventedWorlds.Clear(); ResetResearch(); ResetQuarantines(); ResetPlannedCare();
    }
    private void AddLog(string text) { _log.Insert(0, text); if (_log.Count > 12) _log.RemoveRange(12, _log.Count - 12); }
    public List<string> GetLog() => new(_log);
    private float Weighted(Func<World, float> value, string? sector = null)
    {
        var worlds = _worlds.Values.Where(w => sector == null || w.Map.Sector?.Id == sector).ToArray();
        var population = worlds.Sum(w => w.Population);
        return population <= 0 ? 0 : (float) (worlds.Sum(w => value(w) * w.Population) / population);
    }
    private World? ResolveWorld(string? id) => id == null ? null : _worlds.GetValueOrDefault(id) ??
        _worlds.Values.Where(w => w.Map.Sector?.Id == id).OrderByDescending(w => w.Infection).ThenByDescending(w => w.Map.Anchor).FirstOrDefault();
    public float PlanetInfection(string world) => _worlds.GetValueOrDefault(world)?.Infection ?? 0;
    public float SectorInfection(string sector) => Weighted(w => w.Infection, sector);
    public float InfectionAvg() => Weighted(w => w.Infection);
    private float Care(string id) => _worlds.TryGetValue(id, out var world) ? Care(world) : 0;
    private float Care(World world)
    {
        var snapshot = _economy.Snapshot;
        if (!ReferenceEquals(snapshot, _careSnapshot))
        {
            _careSnapshot = snapshot;
            _careSupplied.Clear();
            if (snapshot != null)
                foreach (var group in snapshot.Services.Where(s => s.Group is "care_drugs" or "care_supplies").GroupBy(s => s.World))
                {
                    var services = group.ToDictionary(s => s.Group);
                    var supplied = 1d;
                    foreach (var required in new[] { "care_drugs", "care_supplies" })
                    {
                        var service = services.GetValueOrDefault(required);
                        supplied = Math.Min(supplied, service is { LastRequired: > 1e-9 } ? Math.Clamp(service.LastServed / service.LastRequired, 0, 1) : 0);
                    }
                    _careSupplied[group.Key] = (float) supplied;
                }
        }
        return _careSupplied.GetValueOrDefault(world.Map.Id) * (.25f + .75f * _programs.GetHealthClinicCoverage(world.Map.Id));
    }
    public override void Update(float frameTime)
    {
        SettleMedicalOrders();
        SettlePlannedCare();
        if (!_time.ModeActive) return;
        // The clock reaches the night boundary after simulation systems; release an expiry
        // on the following frame even though active-time progression has now stopped.
        if (_quarantines.Any(q => !q.Pending && q.Until <= _time.ActiveGameHours)) ExpireQuarantines();
        var seconds = _time.ActiveSeconds(frameTime); if (seconds <= 0) return;
        _accum += seconds; if (_accum < 1) return;
        var hours = _accum / SWSenateTimeSystem.RealSecondsPerGameHour; _accum = 0;
        if (_time.Day != _day) { _day = _time.Day; _program = null; _progress = 0; }
        ExpireQuarantines(); ProcessQuarantineAutopilot(); RefreshSharedClinics(); SeedPathogen(hours);
        if (_active)
        {
            SpreadInfection(hours);
            if (MinisterActive && _random.Prob(Math.Clamp((.015f + InfectionAvg() * .0005f) * hours, 0, 1))) Mutate();

            if (_worlds.Values.All(w => w.Infection < .5f)) { _active = false; AddLog($"Эпидемия «{_name}» подавлена. Досье и партии сохранены."); }
        }
        else
        {
            foreach (var world in _worlds.Values) world.Trend = 0;
            AdvanceLocalCare(hours);
        }
        if (!_active) foreach (var w in _worlds.Values) w.IncomingInfectionPerHour = 0;
        PublishInfectionEffects();
        AdvanceDossier(hours);
        if (_program != null) _progress = Math.Clamp(_progress + 100f / (SWSenateTimeSystem.EndHour - SWSenateTimeSystem.StartHour) * hours, 0, 100);
    }
    private void RefreshSharedClinics()
    {
        foreach (var w in _worlds.Values)
        {
            if (_programs.IsHealthProgramCompleted(w.Map.Id, "SWProgramClinics") && _countedClinics.Add(w.Map.Id)) _hospitalsBuilt++;
            if (_programs.IsHealthProgramCompleted(w.Map.Id, "SWProgramVaccination") && _preventedWorlds.Add(w.Map.Id)) w.PendingImmunity += 15;
        }
    }
    private void SeedPathogen(float hours)
    {
        if (_active || _worlds.Count == 0) return;
        var health = _senate.GetRepublicGlobal("SWGlobalHealthcare");
        var scale = Math.Clamp(50 / Math.Max(health, 1), .4f, 3.5f);
        if (_random.Prob(Math.Clamp(.04f * scale * hours, 0, 1))) BeginPathogen(_random.Pick(_worlds.Keys.ToList()));
    }
    private void BeginPathogen(string id)
    {
        var w = ResolveWorld(id); if (w == null) return;
        // Previous exposure offers limited general protection, not permanent immunity to every future pathogen.
        foreach (var previous in _worlds.Values)
        {
            previous.Immunity *= .25f;
            previous.PendingImmunity *= .15f;
        }
        _active = true; _episode++; _name = _random.Pick(PathogenNames); _strain = 0;
        _virulence = MinisterActive ? Math.Clamp(1 + (50 - _senate.GetRepublicGlobal("SWGlobalHealthcare")) / 120, .85f, 1.5f) : .7f;
        _lethality = 1; w.Infection = Math.Max(w.Infection, MinisterActive ? 20 : 8); StartNewDossier();
        AddLog($"Новый патоген «{_name}»: очаг на {w.Map.Name}.");
        RaiseLocalEvent(new SWSenateCustomAnnouncementEvent("Министерство здравоохранения", $"Выявлен очаг «{_name}» на {w.Map.Name}.", SWSenateAnnouncementKind.Health));
    }
    private void SpreadInfection(float hours)
    {
        var incoming = _worlds.Keys.ToDictionary(w => w, _ => 0f); var snapshot = _economy.Snapshot;
        if (MinisterActive && snapshot != null)
        {
            var edges = snapshot.Edges.ToDictionary(e => e.Id);
            var traffic = snapshot.Flows.GroupBy(f => f.Edge).ToDictionary(g => g.Key, g => g.Sum(f => f.Dispatched) / .25);
            foreach (var link in _links)
            {
                if (!edges.TryGetValue(link.ID, out var edge) || edge.CapacitySlotsPerHour <= 0 || QuarantineBlocksContact(link.From, link.To)) continue;
                var intensity = (float) Math.Clamp(traffic.GetValueOrDefault(link.ID) / Math.Max(1, link.CapacitySlotsPerHour), 0, 1);
                intensity *= _transit.HealthContactFactor(_worlds[link.From].Map.Sector!.Value.Id)
                    * _transit.HealthContactFactor(_worlds[link.To].Map.Sector!.Value.Id);
                incoming[link.To] += _worlds[link.From].Infection * intensity * .025f;
                incoming[link.From] += _worlds[link.To].Infection * intensity * .025f;
            }
        }
        foreach (var w in _worlds.Values)
        {
            var before = w.Infection;
            w.IncomingInfectionPerHour = incoming[w.Map.Id];
            w.Infection = SWHealthWorldPolicy.AdvanceInfection(before, w.Immunity, Care(w), incoming[w.Map.Id], _virulence, MinisterActive, hours);
            w.Trend = hours > 0 ? (w.Infection - before) / hours : 0;
        }
        AdvanceLocalCare(hours);
    }
    private void AdvanceLocalCare(float hours)
    {
        foreach (var w in _worlds.Values)
        {
            var result = SWHealthWorldPolicy.AdvanceCare(w.Infection, w.Immunity, w.PendingImmunity, w.Reserve,
                _programs.GetHealthClinicCoverage(w.Map.Id), Care(w), hours);
            if (hours > 0) w.Trend += (result.Infection - w.Infection) / hours;
            w.Infection = result.Infection;
            w.Immunity = result.Immunity;
            w.PendingImmunity = result.PendingImmunity;
            w.Reserve = result.Reserve;
        }
    }
    private void Mutate() { _strain++; _virulence = Math.Min(_virulence * 1.15f, 3); _lethality = Math.Min(_lethality * 1.1f, 2.5f); AddLog($"Мутация: штамм {_strain + 1}. Досье и партии сохранены."); }
    // An epidemic is a current condition, not a permanent policy change. Clinic and
    // supply effects remain in their existing sources and are not repeated here.
    private void PublishInfectionEffects()
    {
        var contributions = new List<SWSituationalContribution>();
        foreach (var sector in _worlds.Values.Select(w => w.Map.Sector!.Value.Id).Distinct())
        {
            var severity = Math.Clamp((SectorInfection(sector) - 50f) / 50f * _lethality, 0f, 1f);
            if (severity <= 0) continue;
            contributions.Add(new SWSituationalContribution(sector, "SWStatEpidemic", -6f * severity));
            contributions.Add(new SWSituationalContribution(sector, "SWStatLiving", -4f * severity));
        }
        _senate.ReplaceSituationalContributions("ministry.health.infection", contributions);
    }
    private bool BuildClinic(string world) => CanManage && _worlds.ContainsKey(world) && _programs.ApplyFromHealth(world, "SWProgramClinics");
    public bool BuildHospital(string id) => CanManage && ResolveWorld(id) is { } w && _programs.ApplyFromHealth(w.Map.Id, "SWProgramClinics");
    public bool EmergencyAid(string id) => CanManage && ResolveWorld(id) is { } w && SendAid(w.Map.Id);
    public void SelectProgram(string id) { }
    private void ApplyProgram()
    {
        if (_program == null || !_proto.TryIndex<SWHealthProgramPrototype>(_program, out var p)) return;
        foreach (var (stat, delta) in p.PolicyEffects) _senate.AddPolicy(stat, delta * _progress / 100);
        if (p.SpreadDelta != 0) AddInfectionAll(p.SpreadDelta * _progress / 100);
        if (_active && _samples.Count > 0 && _stage <= SWHealthStage.Analysis)
            _stageProgress = Math.Clamp(_stageProgress + p.ResearchDelta * _progress / 100, 0, 99);
    }
    public (float Cost, float Effect) GetRequestModifier(string? sector)
    {
        if (sector == null) return (1, 1);
        var coverage = Weighted(w => _programs.GetHealthClinicCoverage(w.Map.Id), sector); var severe = SectorInfection(sector) >= 50;
        return ((1 - .25f * coverage) * (severe ? 1.3f : 1), (1 + .25f * coverage) * (severe ? .8f : 1));
    }
    public void OnSectorEngagement(string? sector) { if (sector != null) AddInfection(sector, -12); }
    public bool ForceSeed(string? id)
    {
        var w = id == null && _worlds.Count > 0 ? _random.Pick(_worlds.Values.ToList()) : ResolveWorld(id); if (w == null) return false;
        if (!_active) BeginPathogen(w.Map.Id); else w.Infection = Math.Clamp(w.Infection + 25, 0, 100);
        AddLog($"Администратор: усилен очаг на {w.Map.Name}."); return true;
    }
    public void ClearPlague() { _active = false; foreach (var w in _worlds.Values) w.Infection = w.Trend = w.IncomingInfectionPerHour = 0; AddLog("Администратор: эпидемия ликвидирована; досье и партии сохранены."); }
    public bool SetInfection(string? id, float value)
    {
        if (!float.IsFinite(value)) return false;
        var targets = id == null ? _worlds.Values.ToArray() : _worlds.Values.Where(w => w.Map.Id == id || w.Map.Sector?.Id == id).ToArray();
        if (targets.Length == 0) return false; if (!_active && value > 0) BeginPathogen(targets[0].Map.Id);
        foreach (var w in targets) w.Infection = Math.Clamp(value, 0, 100); AddLog($"Администратор: заражение установлено {value:0}%."); return true;
    }
    public void AddInfectionAll(float delta) { if (float.IsFinite(delta)) foreach (var w in _worlds.Values) w.Infection = Math.Clamp(w.Infection + delta, 0, 100); }
    public void AddInfection(string id, float delta) { if (_active && float.IsFinite(delta)) foreach (var w in _worlds.Values.Where(w => w.Map.Id == id || w.Map.Sector?.Id == id)) w.Infection = Math.Clamp(w.Infection + delta, 0, 100); }
    public bool ForceMutate() { if (!_active) return false; Mutate(); AddLog("Администратор: вызвана мутация."); return true; }
    public string AdminStatus() => _active ? $"«{_name}», штамм {_strain + 1}; заражение {InfectionAvg():0}%; иммунитет {Immunity}%; исследование {Research}%." : $"Эпидемий нет. Иммунитет {Immunity}%.";
    public string PandemicStatus() => !_active ? "Нет вспышек" : InfectionAvg() switch { >= 60 => "Пандемия", >= 35 => "Эпидемия", >= 15 => "Вспышки", _ => "Очаги" };
    public Color PandemicColor() => !_active ? Color.FromHex("#43D16A") : SectorStatusColor(InfectionAvg());
    private static string SectorStatus(float inf) => inf switch { >= 70 => "Катастрофа", >= 40 => "Вспышка", >= 12 => "Очаг", _ => "Чисто" };
    private static Color SectorStatusColor(float inf) => Color.FromHex(inf switch { >= 70 => "#E0556B", >= 40 => "#E67E22", >= 12 => "#E0A23B", _ => "#43D16A" });
    public List<SWInfectionSectorEntry> BuildInfectionSectors()
    {
        var result = new List<SWInfectionSectorEntry>();
        foreach (var sector in _worlds.Values.Select(w => w.Map.Sector!.Value.Id).Distinct())
        {
            if (!_proto.TryIndex<SWSectorPrototype>(sector, out var p)) continue;
            var w = ResolveWorld(sector)!; var clinic = _programs.BuildHealthPrograms(w.Map.Id).FirstOrDefault(p => p.Id == "SWProgramClinics"); var inf = (int) MathF.Round(SectorInfection(sector));
            result.Add(new SWInfectionSectorEntry { Id = sector, Name = p.Name, Color = p.Color, Infection = inf, StatusLabel = SectorStatus(inf), StatusColor = SectorStatusColor(inf), Quarantined = IsQuarantined(sector),
                HospitalLevel = (int) MathF.Round(Weighted(w => _programs.GetHealthClinicCoverage(w.Map.Id), sector) * HospitalMaxLevel), Building = clinic is { Started: true, Applied: false },
                BuildLeft = clinic == null ? 0 : Math.Max(0, clinic.WorkHours - clinic.WorkedHours), CanBuild = CanManage && clinic?.Affordable == true, BuildCost = clinic?.Cost ?? 0,
                CanAid = CanManage && _dilemma.GetMinistryBudget(HealthMinistryId) >= AidCost, AidCost = AidCost });
        }
        return result.OrderByDescending(s => s.Infection).ToList();
    }
    public List<SWDefenseProgramEntry> BuildPrograms()
    {
        var list = new List<SWDefenseProgramEntry>();
        foreach (var proto in _proto.EnumeratePrototypes<SWHealthProgramPrototype>().OrderBy(p => p.Order))
        {
            var entry = new SWDefenseProgramEntry
            {
                Id = proto.ID,
                Name = proto.Name,
                Description = proto.Description,
                Selected = proto.ID == _program,
                Effects = PolicyEffectEntries(proto.PolicyEffects),
            };

            if (proto.ResearchDelta != 0f)
                entry.Effects.Add(new SWBillEffectEntry { StatName = "Исследование данных", Color = Color.FromHex("#48C9B0"), Delta = proto.ResearchDelta });
            if (proto.SpreadDelta != 0f)
                entry.Effects.Add(new SWBillEffectEntry { StatName = "Заражение", Color = Color.FromHex("#E0556B"), Delta = proto.SpreadDelta });

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
