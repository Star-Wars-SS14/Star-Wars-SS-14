using System.Linq;
using Content.Server._SW._Senate.Economy;
using Content.Server._SW._Senate.Economy.Simulation;
using Content.Shared._SW._Senate.Economy;
using Content.Shared._SW._Senate.Ministries;

namespace Content.Server._SW._Senate.Core;

public sealed partial class SWMinistryHealthSystem
{
    private sealed class MedicalOrder(string id, string producer, string target, int episode, int strain, int candidate, int quantity, bool aid, bool extended = false)
    {
        public readonly string Id = id;
        public readonly string Producer = producer;
        public readonly string Target = target;
        public readonly int Episode = episode;
        public readonly int Strain = strain;
        public readonly int Candidate = candidate;
        public readonly int Quantity = quantity;
        public readonly bool Aid = aid;
        public readonly bool Extended = extended;
        public bool Applied;
    }
    private readonly List<MedicalOrder> _medicalOrders = new();
    private readonly HashSet<string> _samples = new();
    private SWHealthStage _stage;
    private float _stageProgress;
    private int _funding;
    private float _researchCredit;
    private int _focus;
    private int _candidate;
    private int _readyCandidate;
    private int _researchStrain;
    private int _researchEpisode;
    private int _readyStrain = -1;
    private int _readyEpisode = -1;
    private bool _extended;
    private bool _extensionComplete;
    private bool _readyExtended;
    private readonly List<float> _trialTreated = new();
    private readonly List<float> _trialControl = new();
    private string _source = "";
    private string _laboratory = "";
    private string? _trialProject;
    private EconomyAmount[] _pendingTrialMaterials = Array.Empty<EconomyAmount>();
    private int _nextMedicalOrder;
    private EconomySnapshot? _settledSnapshot;
    public int Research => _readyStrain >= 0 && _readyEpisode == _episode ? 100 : (int) Math.Min(99, ((int) _stage * 100 + _stageProgress) / 4);
    public bool VaccineReady => _readyStrain >= 0 && _readyEpisode == _episode;
    public bool VaccineStale => VaccineReady && _readyStrain != _strain;
    public bool CanDeployVaccinationTeams => _time.ModeActive && _time.DayActive
        && _worlds.Values.Any(w => w.PendingImmunity > .01f && w.Immunity < 100 && Care(w) > 0);
    public bool DeployVaccinationTeams()
    {
        if (!CanDeployVaccinationTeams) return false;
        foreach (var world in _worlds.Values)
        {
            var used = Math.Min(world.PendingImmunity, Math.Min(100 - world.Immunity, 6 * Care(world)));
            world.PendingImmunity -= used;
            world.Immunity += used;
        }
        AddLog("Выездные бригады ускорили вакцинацию из доставленных запасов.");
        return true;
    }
    public int ResearchFunding => _funding;
    public string ResearchFundingLabel() => _funding switch { 1 => "Базовое", 2 => "Усиленное", _ => "Остановлено" };
    public void SetFunding(int level) { if (CanManage && level is >= 0 and <= 2) _funding = level; }
    // Kept for older callers; no endpoint may conjure doses or apply a republic-wide instant vaccine.
    public bool DeployVaccine() => false;
    private void ResetResearch()
    {
        _medicalOrders.Clear(); _nextMedicalOrder = 0; _settledSnapshot = null; _funding = 0; _researchCredit = 0;
        StartNewDossier();
    }
    private void StartNewDossier()
    {
        _samples.Clear(); _stage = SWHealthStage.Samples; _stageProgress = 0; _focus = _candidate = 0;
        _readyStrain = _readyEpisode = -1; _extended = _extensionComplete = _readyExtended = false; _source = _laboratory = ""; _trialProject = null;
        _trialTreated.Clear(); _trialControl.Clear();
        _pendingTrialMaterials = Array.Empty<EconomyAmount>();
        _researchEpisode = _episode; _researchStrain = _strain;
    }
    public bool HandleAction(SWHealthActionMessage message, EntityUid actor)
    {
        if (!CanManage || !_ministry.IsActiveHolder(HealthMinistryId, actor) || !Enum.IsDefined(message.Action)) return false;
        return message.Action switch
        {
            SWHealthAction.CollectSample => CollectSample(message.WorldId),
            SWHealthAction.FocusAnalysis => SetFocus(message.Value),
            SWHealthAction.SelectCandidate => SetCandidate(message.Value),
            SWHealthAction.StartTrials => StartTrials(message.WorldId),
            SWHealthAction.ExtendTrials => ExtendTrials(),
            SWHealthAction.OrderBatch => OrderBatch(message.WorldId, message.TargetWorldId, message.Value),
            SWHealthAction.RequestQuarantine => RequestQuarantine(message.WorldId, message.TargetWorldId, message.Value, message.Option, actor),
            SWHealthAction.LiftQuarantine => LiftQuarantine(message.Value),
            SWHealthAction.BuildClinic => BuildClinic(message.WorldId),
            SWHealthAction.SendAid => SendAid(message.WorldId),
            SWHealthAction.StartPrevention => _worlds.ContainsKey(message.WorldId) && _programs.ApplyFromHealth(message.WorldId, "SWProgramVaccination"),
            SWHealthAction.SetFunding => ChangeFunding(message.Value),
            SWHealthAction.StartPlannedCare => StartPlannedCare(message.WorldId, message.Option),
            _ => false,
        };
    }
    private bool ChangeFunding(int level) { if (level is < 0 or > 2) return false; SetFunding(level); return true; }
    private bool SetFocus(int focus) { if (focus is < 0 or > 2 || _stage > SWHealthStage.Analysis) return false; _focus = focus; return true; }
    private bool SetCandidate(int candidate)
    {
        if (candidate is < 0 or > 1 || _trialProject != null
            || (_stage != SWHealthStage.Candidate && (_stage != SWHealthStage.Trials || VaccineReady))) return false;
        _candidate = candidate;
        _stage = SWHealthStage.Trials;
        return true;
    }
    private bool CollectSample(string world)
    {
        if (!_active || !_worlds.TryGetValue(world, out var source) || source.Infection < .5f) return false;
        if (VaccineStale && _stage >= SWHealthStage.Production)
        {
            _samples.Clear(); _stage = SWHealthStage.Samples; _stageProgress = 50; _extended = _extensionComplete = false; _trialProject = null;
            _researchStrain = _strain; _researchEpisode = _episode;
        }
        if (_stage > SWHealthStage.Analysis || _samples.Count >= 2 || !_samples.Add(world)) return false;
        if (_source.Length == 0 || _samples.Count == 1) _source = world;
        _researchStrain = _strain;
        AddLog($"Лаборатория получает наблюдения: {source.Map.Name}.");
        return true;
    }
    private void AdvanceDossier(float hours)
    {
        if (!MinisterActive || _funding == 0 || _samples.Count == 0 || _stage > SWHealthStage.Analysis) return;
        var funded = _dilemma.FundWork(HealthMinistryId, (_funding == 1 ? 60 : 140) * hours, ref _researchCredit);
        _stageProgress = Math.Min(100, _stageProgress + hours * 200 * ResearchSpeed() * funded);
        if (_stageProgress < 100) return;
        _stage = _stage == SWHealthStage.Samples ? SWHealthStage.Analysis : SWHealthStage.Candidate;
        _stageProgress = 0;
    }
    private float ResearchSpeed() => (_funding == 2 ? 1.6f : 1)
        * Math.Clamp(_senate.GetRepublicGlobal("SWGlobalHealthcare") / 50, .5f, 1.5f);
    private bool CanResearchAt(string world) => _worlds.ContainsKey(world) && (Care(world) > .1f || _programs.GetHealthClinicCoverage(world) > 0);
    private bool CanProduceAt(string world) => VaccineProductionUnavailableReason(world) == null;
    private bool QueuePaid(EconomyProjectRequest request, int cost)
    {
        var before = _dilemma.GetMinistryBudget(HealthMinistryId);
        if (cost < 0 || !_dilemma.SpendMinistry(HealthMinistryId, cost)) return false;
        if (_economy.QueueProject(request)) return true;
        _dilemma.SetMinistryBudget(HealthMinistryId, before);
        return false;
    }
    private static EconomyAmount[] TrialMaterialRequirements(bool extensionOnly = false) => extensionOnly
        ? new[] { new EconomyAmount("medical_supplies", .1) }
        : new[] { new EconomyAmount("lab_equipment", .05), new EconomyAmount("medical_supplies", .1) };

    private int TrialCost => (_candidate == 0 ? 300 : 450) + (_extended ? 150 : 0);
    private bool StartTrials(string world)
    {
        if (_stage != SWHealthStage.Trials || !CanResearchAt(world) || _trialProject != null) return false;
        var id = $"health-trial:{++_nextMedicalOrder}";
        var request = new EconomyProjectRequest(id, world, TrialMaterialRequirements(),
            (_candidate == 0 ? 1.5 : 2.5) + (_extended ? .75 : 0), Medical: new EconomyMedicalProject(EconomyMedicalProjectKind.Research, $"{_researchEpisode}:{_researchStrain}", world, 1));
        if (!QueuePaid(request, TrialCost)) return false;
        _pendingTrialMaterials = request.Materials.ToArray();
        _laboratory = world; _trialProject = id; _stage = SWHealthStage.Trials; _stageProgress = 0;
        _trialControl.Clear(); _trialTreated.Clear();
        AddLog($"Испытания препарата: {_worlds[world].Map.Name}.");
        return true;
    }
    private bool ExtendTrials()
    {
        if (_extended) return false;
        if (_stage == SWHealthStage.Trials && _trialProject == null && !VaccineReady)
        { _extended = true; return true; }
        if (_stage < SWHealthStage.Production || !VaccineReady || !CanResearchAt(_laboratory)) return false;
        var id = $"health-trial:{++_nextMedicalOrder}";
        if (!QueuePaid(new EconomyProjectRequest(id, _laboratory, TrialMaterialRequirements(extensionOnly: true), .75,
                Medical: new EconomyMedicalProject(EconomyMedicalProjectKind.Research, $"{_researchEpisode}:{_researchStrain}", _laboratory, 1)), 150)) return false;
        _pendingTrialMaterials = TrialMaterialRequirements(extensionOnly: true);
        _extended = true; _trialProject = id; _stage = SWHealthStage.Trials; _stageProgress = 0;
        return true;
    }
    private bool OrderBatch(string producer, string target, int quantity)
    {
        if (!VaccineReady || !CanProduceAt(producer) || !_worlds.ContainsKey(target) || quantity is < 1 or > 3
            || _medicalOrders.Count(o => !o.Applied && !o.Aid) >= 6
            || _medicalOrders.Any(o => !o.Applied && !o.Aid && o.Target == target)) return false;
        var id = $"health-vaccine:{++_nextMedicalOrder}";
        var request = SWRepublicMedicalLogistics.CreateVaccineProject(id, producer, target, $"{_readyEpisode}:{_readyStrain}", quantity);
        if (_readyCandidate == 1)
            request = request with { Materials = request.Materials.Select(m => m.Good == "medical_biomass" ? m with { Quantity = m.Quantity * 1.4 } : m).ToArray() };
        if (!QueuePaid(request, quantity * (_readyCandidate == 0 ? 1200 : 1600))) return false;
        _medicalOrders.Add(new MedicalOrder(id, producer, target, _readyEpisode, _readyStrain, _readyCandidate, quantity, false, _readyExtended));
        _stage = SWHealthStage.Delivery;
        AddLog($"Заказано {quantity} партий для {_worlds[target].Map.Name}.");
        return true;
    }
    public bool SendAid(string world)
    {
        if (!CanManage || !_worlds.TryGetValue(world, out var target) || target.Reserve >= 3.5f
            || _medicalOrders.Any(o => o.Aid && !o.Applied && o.Target == world)) return false;
        var id = $"health-relief:{++_nextMedicalOrder}";
        var request = new EconomyProjectRequest(id, world,
            new[] { new EconomyAmount("drugs", .25), new EconomyAmount("medical_supplies", .35) }, .25,
            Medical: new EconomyMedicalProject(EconomyMedicalProjectKind.Relief, "", world, 1));
        if (!QueuePaid(request, AidCost)) return false;
        _medicalOrders.Add(new MedicalOrder(id, world, world, _episode, _strain, 0, 1, true));
        AddLog($"Заказ помощи: {target.Map.Name}.");
        return true;
    }
    internal static float VaccineEfficacy(int candidate, int strainGap, bool extended = false)
    {
        var gap = Math.Max(0, strainGap) * (extended ? .75f : 1);
        return candidate == 0 ? 1 / (1 + .6f * gap) : .85f / (1 + .15f * gap);
    }
    private void SettleMedicalOrders()
    {
        var snapshot = _economy.Snapshot;
        if (snapshot == null || ReferenceEquals(snapshot, _settledSnapshot)) return;
        _settledSnapshot = snapshot;
        if (_trialProject != null && snapshot.Projects.FirstOrDefault(p => p.Id == _trialProject) is { } trial)
        {
            _stageProgress = (float) Math.Clamp(trial.WorkDone / trial.WorkHours * 100, 0, 100);
            // Capture observations once at each sampled work milestone. Later mutations do not rewrite history.
            var points = trial.WorkDone > 0 ? (int) (_stageProgress / 100 * 6) + 1 : 0;
            while (_trialControl.Count < points)
            {
                var index = _trialControl.Count;
                _trialControl.Add(Math.Min(100, 50 + index * _virulence * 4));
                _trialTreated.Add(Math.Max(0, 50 - index * 7 * VaccineEfficacy(_candidate, _strain - _researchStrain, _extended)));
            }
            if (trial.CompletedHour != null)
            {
                _readyStrain = _researchStrain; _readyEpisode = _researchEpisode; _readyCandidate = _candidate;
                _validatedDossiers.Add((_researchEpisode, _researchStrain));
                _extensionComplete = _readyExtended = _extended;
                _trialProject = null; _stage = SWHealthStage.Production;
                AddLog($"Препарат для штамма {_readyStrain + 1} готов к выпуску.");
            }
        }
        foreach (var order in _medicalOrders.Where(o => !o.Applied))
        {
            if (snapshot.Projects.FirstOrDefault(p => p.Id == order.Id) is not { CompletedHour: not null } project) continue;
            order.Applied = true;
            if (!_worlds.TryGetValue(order.Target, out var world)) continue;
            if (order.Aid) world.Reserve = Math.Min(8, world.Reserve + 4);
            else if (order.Episode == _episode)
            {
                var amount = (float) Math.Clamp(project.DeliveredQuantity, 0, order.Quantity);
                world.PendingImmunity = Math.Min(100 - world.Immunity,
                    world.PendingImmunity + SWHealthWorldPolicy.DoseCoverage(amount, world.Population)
                        * VaccineEfficacy(order.Candidate, _strain - order.Strain, order.Extended));
            }
            AddLog($"{(order.Aid ? "Медицинская помощь" : "Партия препарата")} доставлена: {world.Map.Name}.");
        }
    }
    private double StockHours(string world, string group)
    {
        var demand = _economy.Definition?.Demands.Where(d => d.World == world && d.Group == group).ToArray();
        if (demand == null || demand.Length == 0 || _economy.Snapshot == null) return -1;
        var rate = demand.Sum(d => d.Rate);
        if (rate <= 0) return -1;
        // Nutrition goods substitute each other: sum available food and divide by the total food demand.
        return demand.Sum(d => _economy.Snapshot.Inventory.FirstOrDefault(i => i.World == world && i.Good == d.Good)?.Stock ?? 0) / rate;
    }
    private SWHealthOrderView OrderView(MedicalOrder order)
    {
        var project = _economy.GetProject(order.Id);
        var payload = project?.PayloadDelivery is { } delivery ? MaterialView(delivery) : null;
        if (payload != null)
        {
            payload.Name = "Готовая вакцина";
            payload.StockKnown = false;
        }
        return new SWHealthOrderView
        {
            Id = order.Id, Name = order.Aid ? "Медицинская помощь" : "Вакцина", ProducerWorldId = order.Producer, TargetWorldId = order.Target,
            Strain = order.Strain + 1, Completed = order.Applied,
            Progress = project == null ? 0 : (float) (project.WorkDone / project.WorkHours * 100),
            Status = order.Applied ? "Доставлено" : project == null ? "Заказ принят" : project.Status switch
            {
                EconomyProjectStatus.Working => project.Reason switch
                {
                    "power" => "Недостаточно энергии", "labor" => "Не хватает персонала",
                    "production_queue" => "Ожидание производственной линии", _ => "Производство",
                },
                EconomyProjectStatus.Delivering => project.Reason switch
                {
                    "no_route" => "Доставка остановлена: маршрут недоступен",
                    "transport" => "Доставка ожидает транспорт", "fuel" => "Доставка ожидает топливо", _ => "Доставка",
                },
                EconomyProjectStatus.Completed => "Поступило", _ => project.Reason switch
                {
                    "no_route" => "Снабжение остановлено: маршрут недоступен",
                    "transport" => "Снабжение ожидает транспорт", "fuel" => "Снабжение ожидает топливо", _ => "Ожидание материалов",
                },
            },
            Materials = project?.Materials.Select(MaterialView).ToList() ?? new(),
            PayloadDelivery = payload,
        };
    }
    private string TrialStatus(EconomyProjectState? trial)
    {
        if (_trialProject == null) return VaccineReady ? "Испытания завершены" : "Готов к испытаниям";
        if (!_time.DayActive) return "Ночная пауза";
        if (trial == null) return "Заказ лаборатории принят";
        return trial.Status switch
        {
            EconomyProjectStatus.Completed => "Испытания завершены",
            EconomyProjectStatus.Working => trial.Reason switch
            {
                "power" => "Недостаточно энергии",
                "labor" => "Не хватает персонала",
                "production_queue" => "Ожидание свободной лаборатории",
                _ => "Испытания идут",
            },
            _ => trial.Reason switch
            {
                "no_route" => "Снабжение остановлено: маршрут недоступен",
                "transport" => "Снабжение ожидает транспорт",
                "fuel" => "Снабжение ожидает топливо",
                _ => "Ожидание материалов лаборатории",
            },
        };
    }
    public SWHealthView BuildHealthView()
    {
        var measured = _stage >= SWHealthStage.Candidate;
        var trial = _trialProject == null ? null : _economy.GetProject(_trialProject);
        var result = new SWHealthView
        {
            CanManage = CanManage, MinisterActive = MinisterActive, PathogenActive = _active, PathogenName = _name, Strain = _strain + 1,
            Budget = _dilemma.GetMinistryBudget(HealthMinistryId), AidCost = AidCost, BatchCost = _readyCandidate == 0 ? 1200 : 1600,
            ActiveHours = _time.ActiveGameHours, Log = GetLog(), Quarantines = BuildQuarantines(),
            Research = new SWHealthResearchView
            {
                Stage = _stage, Progress = _stageProgress, Funding = _funding, FundingPerHour = _funding == 0 ? 0 : _funding == 1 ? 60 : 140,
                Focus = _focus, Candidate = _candidate, Samples = _samples.Count, Extended = _extended, ExtensionComplete = _extensionComplete, Ready = VaccineReady, Stale = VaccineStale,
                TrialsRunning = _trialProject != null, Strain = _researchStrain + 1, SourceWorldId = _source, LaboratoryWorldId = _laboratory,
                TrialCost = TrialCost, TrialStatus = TrialStatus(trial),
                TrialMaterials = TrialMaterialViews(trial),
                HoursLeft = trial == null ? _trialProject != null ? -1 : _stage <= SWHealthStage.Analysis
                    ? !CanManage || _funding == 0 || _samples.Count == 0 || _dilemma.GetMinistryBudget(HealthMinistryId) <= 0
                        ? -1 : (100 - _stageProgress) / (200 * ResearchSpeed()) : 0
                    : trial.Status == EconomyProjectStatus.WaitingMaterials ? -1 : Math.Max(0, trial.WorkHours - trial.WorkDone),
                Status = _stage switch { SWHealthStage.Samples => _samples.Count == 0 ? "Выберите источник наблюдений" : "Сбор наблюдений", SWHealthStage.Analysis => "Анализ данных",
                    SWHealthStage.Candidate => "Выберите препарат и лабораторию", SWHealthStage.Trials => TrialStatus(trial),
                    SWHealthStage.Production => "Готов к выпуску", _ => "Производство и доставка" },
                Spread = measured || _stage == SWHealthStage.Analysis && (_focus == 0 || _samples.Count == 2) ? Math.Min(100, _virulence * 40) : -1,
                Severity = measured || _stage == SWHealthStage.Analysis && (_focus == 1 || _samples.Count == 2) ? Math.Min(100, _lethality * 35) : -1,
                Variability = measured || _stage == SWHealthStage.Analysis && (_focus == 2 || _samples.Count == 2) ? Math.Min(100, 20 + _strain * 12) : -1,
                TrialControl = new(_trialControl), TrialTreated = new(_trialTreated),
            },
            Orders = _medicalOrders.TakeLast(30).Select(OrderView).ToList(),
            Care = BuildPlannedCareView(),
        };
        foreach (var (id, world) in _worlds.OrderBy(p => p.Value.Map.Name))
            result.Worlds.Add(new SWHealthWorldView
            {
                Id = id, Name = world.Map.Name, SectorId = world.Map.Sector!.Value.Id, SectorName = _proto.Index(world.Map.Sector.Value).Name,
                SystemId = world.Map.SystemId, SystemName = world.Map.SystemName,
                Infection = world.Infection, Immunity = world.Immunity, BatchCoverage = SWHealthWorldPolicy.DoseCoverage(1, world.Population), Trend = world.Trend, CareCoverage = Care(id) * 100,
                ClinicCoverage = _programs.GetHealthClinicCoverage(id) * 100, ReserveHours = world.Reserve, FoodHours = StockHours(id, "nutrition"), MedicineHours = StockHours(id, "care_drugs"),
                CanResearch = CanResearchAt(id), CanProduce = CanProduceAt(id), Quarantined = WorldQuarantined(id), Programs = _programs.BuildHealthPrograms(id),
            });
        PublishProductionAvailability(result.Worlds);
        PublishOutbreakDistances(result.Worlds);
        PublishVaccinationTelemetry(result);
        return result;
    }
}
