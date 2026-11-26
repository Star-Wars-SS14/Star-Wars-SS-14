using System.Linq;
using Content.Server._SW._Senate.Economy;
using Content.Server._SW._Senate.Economy.Simulation;
using Content.Shared._SW._Senate.Ministries;

namespace Content.Server._SW._Senate.Core;

public sealed partial class SWMinistryHealthSystem
{
    // Economic lots are abstract. Each course uses .001 lot of each ingredient in
    // the existing two-ingredient treatment bundle. Routine care remains untouched.
    private const double CareBundlePerCourse = .002;
    private const int CareTeamLimit = 3;
    private const int CareOrderLimit = 6;
    private sealed class CareQueue(double rate)
    {
        public readonly double CoursesPerHour = rate;
        // The round opens with four hours of elective cases awaiting treatment.
        public double Waiting = rate * 4;
        public double Unserved;
    }
    private sealed class CareCampaign(string id, string world, bool prosthetic, int courses)
    {
        public readonly string Id = id;
        public readonly string World = world;
        public readonly bool Prosthetic = prosthetic;
        public readonly int Courses = courses;
        public double Applied;
        public bool Completed;
    }
    private readonly Dictionary<(string World, bool Prosthetic), CareQueue> _plannedCare = new();
    private readonly List<CareCampaign> _careCampaigns = new();
    private readonly HashSet<(int Episode, int Strain)> _validatedDossiers = new();
    private EconomySnapshot? _plannedCareSnapshot;
    private int _nextCareCampaign;

    public int ProtectedWorlds => _worlds.Values.Count(w => w.Immunity >= 10);
    public int ValidatedVaccines { get { SettleMedicalOrders(); return _validatedDossiers.Count; } }
    public int CompletedPreventionCampaigns => _preventedWorlds.Count;
    public int CompletedElectiveCareCampaigns { get { SettlePlannedCare(); return _careCampaigns.Count(c => c.Completed); } }
    public float HealthProtectionAchievements => Math.Max(ValidatedVaccines, CompletedPreventionCampaigns / 3f);

    private void ResetPlannedCare()
    {
        _plannedCare.Clear();
        _careCampaigns.Clear();
        _validatedDossiers.Clear();
        _plannedCareSnapshot = null;
        _nextCareCampaign = 0;
    }

    private void SettlePlannedCare()
    {
        var snapshot = _economy.Snapshot;
        if (snapshot == null || ReferenceEquals(snapshot, _plannedCareSnapshot) || _economy.Definition == null) return;
        _plannedCareSnapshot = snapshot;
        if (_plannedCare.Count == 0)
        {
            foreach (var demand in _economy.Definition.Demands
                         .Where(d => _worlds.ContainsKey(d.World) && d.Group is "regenerative_care" or "implant_care")
                         .GroupBy(d => (d.World, Prosthetic: d.Group == "implant_care")))
            {
                var rate = demand.Sum(d => d.Rate) / CareBundlePerCourse;
                if (rate > 0) _plannedCare[demand.Key] = new CareQueue(rate);
            }
        }
        foreach (var service in snapshot.Services.Where(s => s.Group is "regenerative_care" or "implant_care"))
        {
            if (!_plannedCare.TryGetValue((service.World, service.Group == "implant_care"), out var queue)) continue;
            var unserved = Math.Max(0, service.Required - service.Served) / CareBundlePerCourse;
            // Cumulative counters survive skipped publications; reopening the console
            // cannot add the same patients twice. Fully served current care adds none.
            queue.Waiting += Math.Max(0, unserved - queue.Unserved);
            queue.Unserved = unserved;
        }
        foreach (var campaign in _careCampaigns.Where(c => !c.Completed))
        {
            if (snapshot.Projects.FirstOrDefault(p => p.Id == campaign.Id) is not { } project) continue;
            var completed = campaign.Courses * Math.Clamp(project.WorkDone / project.WorkHours, 0, 1);
            var queue = _plannedCare[(campaign.World, campaign.Prosthetic)];
            queue.Waiting = Math.Max(0, queue.Waiting - Math.Max(0, completed - campaign.Applied));
            campaign.Applied = completed;
            if (project.CompletedHour == null) continue;
            campaign.Completed = true;
            AddLog($"{CareName(campaign.Prosthetic)} завершено: {_worlds[campaign.World].Map.Name}, {campaign.Courses} курсов.");
        }
    }

    // A bounded, reversible healthcare burden: removing the queue removes this
    // contribution. No permanent policy bonus or artificial economic production.
    public float PlannedCarePenalty(string sector)
    {
        SettlePlannedCare();
        var queues = _plannedCare.Where(p => _worlds[p.Key.World].Map.Sector?.Id == sector).Select(p => p.Value).ToArray();
        var rate = queues.Sum(q => q.CoursesPerHour);
        // A fraction below one admissible course must not leave an incurable penalty.
        return rate <= 0 ? 0 : (float) -Math.Clamp(queues.Sum(q => q.Waiting + 1e-8 >= 1 ? q.Waiting : 0) / rate / 4, 0, 3);
    }

    private static string CareName(bool prosthetic) => prosthetic ? "Протезирование" : "Восстановительное лечение";
    private double ReservedCare(string world, bool prosthetic) => _careCampaigns
        .Where(c => !c.Completed && c.World == world && c.Prosthetic == prosthetic).Sum(c => c.Courses - c.Applied);
    private int CareSession(string world, bool prosthetic) => _plannedCare.TryGetValue((world, prosthetic), out var queue)
        ? (int) Math.Clamp(Math.Floor(queue.Waiting - ReservedCare(world, prosthetic) + 1e-8), 0, 24) : 0;
    private static int CareCost(bool prosthetic, int courses) => (prosthetic ? 250 : 200) + courses * (prosthetic ? 15 : 10);
    private string CareUnavailable(string world, bool prosthetic, int courses)
    {
        if (!CanManage) return _time.ModeActive && !_time.DayActive ? "Ночная пауза" : "Требуется действующий министр";
        if (_careCampaigns.Any(c => !c.Completed && c.World == world && c.Prosthetic == prosthetic)) return "Кампания уже заказана";
        if (courses < 1) return _plannedCare.TryGetValue((world, prosthetic), out var queue) && queue.Waiting > .001
            ? "Меньше одного курса в очереди" : "Нет ожидающих лечения";
        if (_careCampaigns.Count(c => !c.Completed) >= CareOrderLimit) return "Очередь кампаний заполнена";
        if (_dilemma.GetMinistryBudget(HealthMinistryId) < CareCost(prosthetic, courses)) return "Недостаточно средств";
        return "";
    }
    private bool StartPlannedCare(string world, bool prosthetic)
    {
        SettlePlannedCare();
        var courses = CareSession(world, prosthetic);
        if (!_worlds.ContainsKey(world) || CareUnavailable(world, prosthetic, courses) != "") return false;
        var id = $"health-care-{++_nextCareCampaign}";
        var project = SWRepublicMedicalLogistics.CreateCareProject(id, world, prosthetic, courses);
        if (!QueuePaid(project, CareCost(prosthetic, courses))) return false;
        _careCampaigns.Add(new CareCampaign(id, world, prosthetic, courses));
        AddLog($"Заказано {CareName(prosthetic).ToLowerInvariant()}: {_worlds[world].Map.Name}, {courses} курсов.");
        return true;
    }

    private SWHealthCareView BuildPlannedCareView()
    {
        SettlePlannedCare();
        var result = new SWHealthCareView
        {
            TeamLimit = CareTeamLimit, OrderLimit = CareOrderLimit,
            OpenOrders = _careCampaigns.Count(c => !c.Completed),
            CompletedCampaigns = _careCampaigns.Count(c => c.Completed),
            CompletedCourses = _careCampaigns.Sum(c => c.Applied),
            SectorHealthcareEffects = _worlds.Values.Select(w => w.Map.Sector!.Value.Id).Distinct()
                .ToDictionary(sector => sector, PlannedCarePenalty),
        };
        foreach (var ((world, prosthetic), queue) in _plannedCare.OrderByDescending(p => p.Value.Waiting))
        {
            var courses = CareSession(world, prosthetic);
            var reason = CareUnavailable(world, prosthetic, courses);
            var materials = courses == 0 ? Array.Empty<EconomyAmount>()
                : SWRepublicMedicalLogistics.CreateCareProject("preview", world, prosthetic, courses).Materials;
            result.Needs.Add(new SWHealthCareNeedView
            {
                WorldId = world, Prosthetic = prosthetic, WaitingCourses = queue.Waiting,
                ReservedCourses = ReservedCare(world, prosthetic), SessionCourses = courses,
                WorkHours = prosthetic ? 3 : 2, Cost = courses > 0 ? CareCost(prosthetic, courses) : 0,
                CanStart = reason == "", UnavailableReason = reason,
                Materials = materials.Select(m => MaterialView(new EconomyProjectMaterialState(m.Good, m.Quantity, 0, 0)
                    { DeliveryStatus = "awaiting_order" })).ToList(),
            });
        }
        foreach (var campaign in _careCampaigns.Where(c => !c.Completed).Concat(_careCampaigns.Where(c => c.Completed).TakeLast(8)))
        {
            var project = _economy.GetProject(campaign.Id);
            var working = project is { Status: EconomyProjectStatus.Working } && project.Reason != "medical_teams" && !campaign.Completed;
            if (working) result.BusyTeams++;
            var pending = project == null ? SWRepublicMedicalLogistics.CreateCareProject(campaign.Id, campaign.World, campaign.Prosthetic, campaign.Courses) : null;
            result.Orders.Add(new SWHealthCareOrderView
            {
                Id = campaign.Id, WorldId = campaign.World, Prosthetic = campaign.Prosthetic,
                Courses = campaign.Courses, CompletedCourses = campaign.Applied, Completed = campaign.Completed,
                Progress = (float) (campaign.Applied / campaign.Courses * 100), Working = working,
                HoursLeft = project != null ? Math.Max(0, project.WorkHours - project.WorkDone) : campaign.Prosthetic ? 3 : 2,
                Status = campaign.Completed ? "Завершено" : !_time.DayActive ? "Ночная пауза" : project == null ? "Заказ принят"
                    : project.Reason == "medical_teams" ? "Ожидание свободной бригады" : working ? "Лечение идёт" : project.Reason switch
                    {
                        "no_route" => "Маршрут снабжения недоступен", "transport" => "Ожидание транспорта",
                        "fuel" => "Ожидание топлива", _ => "Сбор и доставка материалов",
                    },
                Materials = project?.Materials.Select(MaterialView).ToList() ?? pending!.Materials.Select(m => MaterialView(
                    new EconomyProjectMaterialState(m.Good, m.Quantity, 0, 0) { DeliveryStatus = "awaiting_publication" })).ToList(),
            });
        }
        return result;
    }
}
