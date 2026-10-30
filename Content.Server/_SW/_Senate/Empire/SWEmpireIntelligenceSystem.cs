using System.Linq;
using Content.Server._SW._Senate.Core;
using Content.Server._SW._Senate.Economy;
using Content.Server._SW._Senate.Economy.Simulation;
using Content.Shared._SW._Senate.Empire;
using Content.Shared._SW._Senate.Economy;
using Content.Shared._SW._Senate.Sectors;
using Content.Shared.GameTicking;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._SW._Senate.Empire;

public sealed class SWEmpireIntelligenceSystem : EntitySystem
{
    private const int MaxGroups = 3, MaxNetworks = 4, MaxReports = 30;
    private const double NetworkFactor = .75, HeavyHours = 2, LightHours = 1, CooldownHours = 3;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly SWRepublicEconomySystem _economy = default!;
    [Dependency] private readonly SWEmpireEconomySystem _imperialEconomy = default!;
    [Dependency] private readonly SWMinistrySystem _ministries = default!;
    [Dependency] private readonly SWEmpireDiplomacySystem _diplomacy = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    private readonly List<Group> _groups = new();
    private readonly HashSet<string> _networks = new();
    private readonly List<SWEmpireIntelligenceReportView> _reports = new();
    private readonly Dictionary<string, Effect> _effects = new();
    private readonly Dictionary<string, double> _cooldowns = new();
    private readonly HashSet<string> _accepted = new();
    private readonly Dictionary<string, SWGalacticMapWorld> _worlds = new();
    private readonly List<SWTransitLinkPrototype> _links = new();
    private SWGalacticMapPrototype? _map;
    private bool _initialized;
    private string _home = "";
    private int _revision, _nextGroup = 1;
    private sealed class Group
    {
        public int Id; public string World = "", Destination = "", Target = ""; public float Readiness = 100;
        public SWEmpireIntelligenceAction Action; public readonly Queue<string> Path = new();
        public double LegStartedAt, LegUntil, WorkUntil; public bool Working; public bool Busy => Working || Path.Count > 0;
    }
    private sealed class Effect { public string Key = "", World = "", Target = "", Name = "", Source = ""; public bool Transit, Weak; public double Ends; }
    public override void Initialize()
    {
        UpdatesBefore.Add(typeof(SWSenateTimeSystem));
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => Reset());
    }
    private void EnsureInitialized()
    {
        if (_initialized) return;
        _map = _proto.Index<SWGalacticMapPrototype>("SenateRepublic");
        foreach (var world in _map.Worlds) _worlds[world.Id] = world;
        // Covert teams may cross an existing closed Empire–Republic border edge;
        // ordinary transit and the fleet retain their independent access checks.
        _links.AddRange(_proto.EnumeratePrototypes<SWTransitLinkPrototype>().Where(x =>
            double.IsFinite(x.TravelHours) && x.TravelHours > 0 && _worlds.ContainsKey(x.From) && _worlds.ContainsKey(x.To) &&
            (x.Accessible || Faction(x.From) == "empire" && Faction(x.To) == "republic" || Faction(x.From) == "republic" && Faction(x.To) == "empire")));
        var republics = _worlds.Values.Where(x => Faction(x.Id) == "republic").ToArray();
        var home = _worlds.Values.Where(x => Faction(x.Id) == "empire")
            .OrderBy(x => x.Id == "dromund_kaas" ? 0 : x.Id == "korriban" ? 1 : 2).ThenBy(x => x.Id)
            .FirstOrDefault(x => republics.Any(r => Path(x.Id,r.Id) != null));
        if (home == null) return;
        _home = home.Id;
        for (var i = 0; i < 2; i++) _groups.Add(new Group { Id = _nextGroup++, World = _home });
        _initialized = true; _revision++;
    }
    private void Reset()
    {
        foreach (var e in _effects.Values) RemoveEffect(e);
        _groups.Clear(); _networks.Clear(); _reports.Clear(); _effects.Clear(); _cooldowns.Clear(); _accepted.Clear(); _worlds.Clear(); _links.Clear();
        _map = null; _initialized = false; _home = ""; _nextGroup = 1; _revision++;
    }
    private string Faction(string world) => _map != null && _worlds.TryGetValue(world, out var w) ? _map.GetWorldFaction(w) : "";
    private string Name(string world) => _worlds.TryGetValue(world, out var w) ? w.Name : "Неизвестный мир";
    private SWTransitLinkPrototype? Link(string a, string b) => _links.FirstOrDefault(x => x.From == a && x.To == b || x.From == b && x.To == a);
    private List<string>? Path(string from, string to)
    {
        if (!_worlds.ContainsKey(from) || !_worlds.ContainsKey(to)) return null;
        var dist = new Dictionary<string,double> { [from] = 0 }; var prev = new Dictionary<string,string>(); var todo = new HashSet<string>(_worlds.Keys);
        while (todo.Count > 0)
        {
            var u = todo.Where(dist.ContainsKey).OrderBy(x => dist[x]).ThenBy(x => x).FirstOrDefault(); if (u == null) break;
            todo.Remove(u); if (u == to) break;
            foreach (var edge in _links.Where(x => x.From == u || x.To == u))
            {
                var v = edge.From == u ? edge.To : edge.From; if (!todo.Contains(v)) continue;
                var d = dist[u] + edge.TravelHours; if (dist.TryGetValue(v,out var old) && old <= d) continue;
                dist[v] = d; prev[v] = u;
            }
        }
        if (!dist.ContainsKey(to)) return null;
        var path = new List<string>(); for (var v = to; v != from; v = prev[v]) path.Add(v); path.Reverse(); return path;
    }
    private double Travel(string from, IEnumerable<string> path) { double h = 0; foreach (var next in path) { h += Link(from,next)?.TravelHours ?? 0; from = next; } return h; }
    private static int Cost(SWEmpireIntelligenceAction a) => a switch { SWEmpireIntelligenceAction.Recon => 200, SWEmpireIntelligenceAction.EstablishNetwork => 400, SWEmpireIntelligenceAction.ProductionSabotage or SWEmpireIntelligenceAction.TransitSabotage => 500, SWEmpireIntelligenceAction.Hire => 800, _ => 0 };
    private static double Work(SWEmpireIntelligenceAction a) => a == SWEmpireIntelligenceAction.Recon ? 1 : a is SWEmpireIntelligenceAction.EstablishNetwork or SWEmpireIntelligenceAction.ProductionSabotage or SWEmpireIntelligenceAction.TransitSabotage ? 2 : 0;
    private static bool Hostile(SWEmpireIntelligenceAction a) => a is SWEmpireIntelligenceAction.ProductionSabotage or SWEmpireIntelligenceAction.TransitSabotage;
    private static string EffectKey(SWEmpireIntelligenceAction a, string world, string target) => a == SWEmpireIntelligenceAction.TransitSabotage ? "route/" + target : "production/" + world + "/" + target;
    private static bool Deny(SWEmpireIntelligenceQuote quote, string reason) { quote.Reason = reason; return false; }
    public bool TryQuote(SWEmpireIntelligenceRequest request, out SWEmpireIntelligenceQuote quote)
    {
        quote = new SWEmpireIntelligenceQuote();
        if (!_initialized) return Deny(quote,"Данные разведки ещё не готовы");

        if (request == null || !_time.ModeActive || !_time.DayActive || !Enum.IsDefined(request.Action) || string.IsNullOrWhiteSpace(request.RequestId) || request.RequestId.Length > 128 || request.WorldId == null || request.WorldId.Length > 128 || request.TargetId == null || request.TargetId.Length > 128 || _accepted.Count >= 4096 || _accepted.Contains(request.RequestId)) return Deny(quote,"Приказ недоступен или уже выполнен");
        quote.Cost = Cost(request.Action);
        if (request.Action == SWEmpireIntelligenceAction.Hire) { if (_groups.Count >= MaxGroups || !_initialized) return Deny(quote,"Все группы уже сформированы"); quote.Allowed = true; quote.Result = "Будет сформирована новая группа"; return true; }
        var group = _groups.Find(x => x.Id == request.GroupId); if (group == null) return Deny(quote,"Группа недоступна");
        if (request.Action != SWEmpireIntelligenceAction.Recall && group.Busy) return Deny(quote,"Группа выполняет задание");
        var targetWorld = request.WorldId;
        if (request.Action == SWEmpireIntelligenceAction.Rest) { if (Faction(group.World) != "empire") return Deny(quote,"Отдых возможен в имперском мире"); targetWorld = group.World; }
        if (request.Action == SWEmpireIntelligenceAction.Recall && Faction(targetWorld) != "empire") return Deny(quote,"Выберите имперский мир для возвращения");
        if (!_worlds.ContainsKey(targetWorld)) return Deny(quote,"Мир недоступен");
        if (request.Action is SWEmpireIntelligenceAction.Recon or SWEmpireIntelligenceAction.EstablishNetwork && Faction(targetWorld) != "republic") return Deny(quote,"Выберите мир Республики");
        if (request.Action == SWEmpireIntelligenceAction.EstablishNetwork && (_networks.Contains(targetWorld) || _networks.Count >= MaxNetworks)) return Deny(quote,"Сеть уже существует или достигнут предел сетей");
        if (Hostile(request.Action))
        {
            if (Faction(targetWorld) != "republic") return Deny(quote,"Диверсия доступна только против Республики");
            if (Blocked(request.Action,targetWorld,request.TargetId,out var treatyReason)) return Deny(quote,treatyReason);
            if (!_reports.Any(r => r.WorldId == targetWorld && r.Targets.Any(t => t.Action == request.Action && t.Id == request.TargetId))) return Deny(quote,"Нужно актуальное донесение о цели");
            var definition = _economy.Definition;
            if (definition == null || (request.Action == SWEmpireIntelligenceAction.ProductionSabotage ? !definition.Production.Any(x => x.World == targetWorld && x.Recipe == request.TargetId) : !definition.Edges.Any(x => x.Id == request.TargetId))) return Deny(quote,"Цель больше недоступна");
            var key = EffectKey(request.Action,targetWorld,request.TargetId);
            if (_groups.Any(x => x.Busy && Hostile(x.Action) && EffectKey(x.Action,x.Destination,x.Target) == key)) return Deny(quote,"Группа уже направлена к этой цели");
            if (_effects.ContainsKey(key) || _cooldowns.GetValueOrDefault(key) > _time.ActiveGameHours) return Deny(quote,"Цель временно недоступна после операции");
        }
        var reroute = request.Action == SWEmpireIntelligenceAction.Recall && group.Path.Count > 0;
        var start = reroute ? group.Path.Peek() : group.World;
        var path = Path(start,targetWorld); if (path == null) return Deny(quote,"Нет действующего маршрута");
        quote.RouteWorldIds.Add(group.World);
        if (reroute && start != group.World) quote.RouteWorldIds.Add(start);
        quote.RouteWorldIds.AddRange(path);
        quote.WorkHours = Work(request.Action) * (_networks.Contains(targetWorld) ? NetworkFactor : 1);
        quote.EtaHours = (reroute ? Math.Max(0,group.LegUntil - _time.ActiveGameHours) : 0) + Travel(start,path) + quote.WorkHours;
        var risk = Math.Clamp(.25f + (100-group.Readiness)/400f - (_networks.Contains(targetWorld) ? .1f : 0), .05f,.5f);
        quote.RiskPercent = request.Action is SWEmpireIntelligenceAction.Recall or SWEmpireIntelligenceAction.Rest ? 0 : risk * 100;
        quote.Risk = quote.RiskPercent <= 0 ? "Нет" : $"{quote.RiskPercent:0}%";
        quote.Result = request.Action switch { SWEmpireIntelligenceAction.Recon => "Датированное донесение о мире", SWEmpireIntelligenceAction.EstablishNetwork => "Сеть снижает риск и время операций", SWEmpireIntelligenceAction.ProductionSabotage => "Временное снижение производства", SWEmpireIntelligenceAction.TransitSabotage => "Временное снижение пропускной способности", SWEmpireIntelligenceAction.Rest => "Восстановление готовности", _ => "Возвращение группы" };
        quote.Allowed = true; return true;
    }
    public bool TrySubmit(SWEmpireIntelligenceRequest request, out string reason)
    {
        if (!TryQuote(request,out var q)) { reason = q.Reason; return false; }
        if (request.Action == SWEmpireIntelligenceAction.Hire)
        { _groups.Add(new Group { Id = _nextGroup++, World = _home }); }
        else
        {
            var group = _groups.First(x => x.Id == request.GroupId); var destination = request.Action == SWEmpireIntelligenceAction.Rest ? group.World : request.WorldId;
            var reroute = request.Action == SWEmpireIntelligenceAction.Recall && group.Path.Count > 0;
            var start = reroute ? group.Path.Peek() : group.World; var path = Path(start,destination)!;
            group.Path.Clear(); if (reroute) group.Path.Enqueue(start); foreach (var step in path) group.Path.Enqueue(step);
            group.Action = request.Action; group.Destination = destination; group.Target = request.TargetId; group.Working = false;
            if (group.Path.Count == 0) BeginWork(group);
            else if (!reroute) { group.LegStartedAt = _time.ActiveGameHours; group.LegUntil = group.LegStartedAt + Link(group.World,group.Path.Peek())!.TravelHours; }
        }
        _accepted.Add(request.RequestId); _revision++; reason = "Приказ принят"; return true;
    }
    private void BeginWork(Group group)
    {
        group.Working = true; group.WorkUntil = _time.ActiveGameHours + Work(group.Action) * (_networks.Contains(group.World) ? NetworkFactor : 1);
    }
    private void Report(Group group, string summary, bool scan = false)
    {
        var report = new SWEmpireIntelligenceReportView { WorldId = group.World, WorldName = Name(group.World), Hour = _time.ActiveGameHours, Summary = summary };
        if (scan && (_economy.Snapshot == null || _economy.Definition == null)) report.Summary = "Разведка проведена, но экономические сведения временно недоступны.";
        if (scan && _economy.Snapshot is { } snapshot && _economy.Definition is { } definition)
        {
            var production = snapshot.Production.Where(p => p.World == group.World).ToArray();
            var shortages = snapshot.Services.Count(s => s.World == group.World && s.Required > 0 && s.Served < s.Required * .9);
            report.Summary = $"Производственных линий: {production.Length}. Служб с нехваткой: {shortages}. Данные на момент разведки; обстановка могла измениться.";
            foreach (var line in production)
            {
                var name = _proto.TryIndex<SWProductionRecipePrototype>(line.Recipe, out var recipe) ? recipe.Name : "Производственная линия";
                report.Targets.Add(new SWEmpireIntelligenceTargetView { Id = line.Recipe, Name = name, Action = SWEmpireIntelligenceAction.ProductionSabotage });
            }
            foreach (var edge in _links.Where(e => (e.From == group.World || e.To == group.World) && Faction(e.From) == "republic" && Faction(e.To) == "republic" && definition.Edges.Any(x => x.Id == e.ID)))
                report.Targets.Add(new SWEmpireIntelligenceTargetView { Id = edge.ID, Name = $"{Name(edge.From)} — {Name(edge.To)}", Action = SWEmpireIntelligenceAction.TransitSabotage });
        }
        _reports.Add(report); if (_reports.Count > MaxReports) _reports.RemoveAt(0); _revision++;
    }
    private bool Blocked(SWEmpireIntelligenceAction action, string world, string target, out string reason)
    {
        if (_diplomacy.BlocksHostileAction(world,out reason)) return true;
        if (action != SWEmpireIntelligenceAction.TransitSabotage) return false;
        if (_diplomacy.BlocksHostileTransit(target,out reason)) return true;
        var edge = _links.FirstOrDefault(x => x.ID == target);
        return edge != null && (_diplomacy.BlocksHostileAction(edge.From,out reason) || _diplomacy.BlocksHostileAction(edge.To,out reason));
    }
    private bool MinisterActive(bool transit) => _ministries.IsHolderActive(transit ? SWMinistryInternalSecuritySystem.SecurityMinistryId : SWMinistryEconomySystem.EconomyMinistryId);
    private bool ApplyEffect(Effect effect)
    {
        var multiplier = effect.Weak ? .9 : .7;
        return effect.Transit
            ? _economy.SetTransitRestrictions(effect.Source, new Dictionary<string,double> { [effect.Target] = multiplier })
            : _economy.SetProductionModifiers(effect.Source, new[] { new EconomyProductionModifier(effect.World,effect.Target,multiplier) });
    }
    private void RemoveEffect(Effect effect)
    {
        if (effect.Transit) _economy.SetTransitRestrictions(effect.Source,new Dictionary<string,double>());
        else _economy.SetProductionModifiers(effect.Source,Array.Empty<EconomyProductionModifier>());
    }
    private void Complete(Group group)
    {
        group.Working = false;
        if (group.Action == SWEmpireIntelligenceAction.Recall) { Report(group,"Группа вернулась в безопасный мир."); return; }
        if (group.Action == SWEmpireIntelligenceAction.Rest) { Report(group,"Группа приступила к восстановлению готовности."); return; }
        if (Faction(group.World) != "republic") { Report(group,"Операция отменена: контроль над миром изменился."); return; }
        var risk = Math.Clamp(.25f + (100-group.Readiness)/400f - (_networks.Contains(group.World) ? .1f : 0), .05f,.5f);
        group.Readiness = Math.Max(0,group.Readiness - 15);
        if (_random.Prob(risk))
        {
            var retreat = _worlds.Values.Where(w => Faction(w.Id) == "empire")
                .Select(w => (World: w.Id, Route: Path(group.World,w.Id)))
                .Where(x => x.Route != null)
                .OrderBy(x => Travel(group.World,x.Route!)).ThenBy(x => x.World).FirstOrDefault();
            if (retreat.Route == null)
            {
                Report(group,"Операция сорвана: группа раскрыта и ожидает эвакуации; безопасный маршрут не найден.");
                return;
            }
            group.Action = SWEmpireIntelligenceAction.Recall; group.Destination = retreat.World; group.Target = "";
            group.Path.Clear(); foreach (var step in retreat.Route) group.Path.Enqueue(step);
            if (group.Path.Count > 0)
            {
                group.LegStartedAt = _time.ActiveGameHours;
                group.LegUntil = group.LegStartedAt + Link(group.World,group.Path.Peek())!.TravelHours;
                Report(group,$"Операция сорвана: группа раскрыта и отступает к миру {Name(retreat.World)}.");
            }
            else Report(group,"Операция сорвана: группа раскрыта, находится в безопасном мире.");
            return;
        }
        switch (group.Action)
        {
            case SWEmpireIntelligenceAction.Recon: Report(group,"",true); break;
            case SWEmpireIntelligenceAction.EstablishNetwork:
                if (_networks.Count < MaxNetworks && _networks.Add(group.World)) Report(group,"Агентурная сеть создана.");
                else Report(group,"Сеть не создана: достигнут предел или сеть уже существует.");
                break;
            case SWEmpireIntelligenceAction.ProductionSabotage:
            case SWEmpireIntelligenceAction.TransitSabotage:
                if (Blocked(group.Action,group.World,group.Target,out _)) { Report(group,"Диверсия отменена: действует соглашение."); break; }
                var transit = group.Action == SWEmpireIntelligenceAction.TransitSabotage;
                var key = EffectKey(group.Action,group.World,group.Target);
                if (_effects.ContainsKey(key) || _cooldowns.GetValueOrDefault(key) > _time.ActiveGameHours) { Report(group,"Диверсия отменена: цель уже подвергается воздействию."); break; }
                var effect = new Effect { Key = key, World = group.World, Target = group.Target, Transit = transit, Source = "empire-intelligence/" + key,
                    Name = _reports.LastOrDefault(r => r.WorldId == group.World)?.Targets.FirstOrDefault(t => t.Id == group.Target && t.Action == group.Action)?.Name ?? "Объект", Weak = !MinisterActive(transit) };
                effect.Ends = _time.ActiveGameHours + (effect.Weak ? LightHours : HeavyHours);
                if (!ApplyEffect(effect)) { Report(group,"Диверсия не выполнена: объект или экономика недоступны."); break; }
                _effects.Add(key,effect); _cooldowns[key] = effect.Ends + CooldownHours;
                Report(group,effect.Weak ? "Диверсия проведена с ограниченным воздействием." : "Диверсия проведена; объект временно ослаблен.");
                break;
        }
    }
    public override void Update(float frameTime)
    {
        if (!_time.ModeActive || !_time.DayActive || _time.ActiveSeconds(frameTime) <= 0) return;
        EnsureInitialized(); var now = _time.ActiveGameHours;
        foreach (var effect in _effects.Values.ToArray())
        {
            if (!effect.Weak && !MinisterActive(effect.Transit))
            { effect.Weak = true; effect.Ends = Math.Min(effect.Ends,now + LightHours); if (!ApplyEffect(effect)) effect.Ends = now; _revision++; }
            var action = effect.Transit ? SWEmpireIntelligenceAction.TransitSabotage : SWEmpireIntelligenceAction.ProductionSabotage;
            if (now < effect.Ends && Faction(effect.World) == "republic" && !Blocked(action,effect.World,effect.Target,out _)) continue;
            RemoveEffect(effect); _effects.Remove(effect.Key); _revision++;
        }
        foreach (var key in _cooldowns.Where(x => x.Value <= now).Select(x => x.Key).ToArray()) _cooldowns.Remove(key);
        foreach (var group in _groups)
        {
            if (group.Path.Count > 0 && now >= group.LegUntil)
            {
                group.World = group.Path.Dequeue();
                if (group.Path.Count > 0) { group.LegStartedAt = now; group.LegUntil = now + Link(group.World,group.Path.Peek())!.TravelHours; }
                else BeginWork(group);
                _revision++;
            }
            if (group.Working && now >= group.WorkUntil) Complete(group);
            if (!group.Busy && Faction(group.World) == "empire" && group.Readiness < 100)
                group.Readiness = Math.Min(100,group.Readiness + _time.ActiveSeconds(frameTime) / SWSenateTimeSystem.RealSecondsPerGameHour * 10);
        }
    }
    public SWEmpireIntelligenceState BuildState()
    {
        EnsureInitialized(); var state = new SWEmpireIntelligenceState { Revision = _revision, NowHours = _time.ActiveGameHours, ClockRunning = _time.ModeActive && _time.DayActive, RealSecondsPerHour = SWSenateTimeSystem.RealSecondsPerGameHour };
        state.Worlds = _worlds.Values.OrderBy(x => x.Name).Select(x => new SWEmpireIntelligenceWorldView { Id = x.Id, Name = x.Name, Sector = x.PoliticalSectorName, Faction = Faction(x.Id) }).ToList();
        state.Networks = _networks.Select(x => new SWEmpireIntelligenceNetworkView { WorldId = x, WorldName = Name(x) }).ToList();
        state.Reports = _reports.ToList();
        foreach (var g in _groups)
        {
            var path = g.Path.ToArray(); var eta = g.Working ? Math.Max(0,g.WorkUntil-_time.ActiveGameHours) : path.Length > 0 ? Math.Max(0,g.LegUntil-_time.ActiveGameHours) + Travel(path[0],path.Skip(1)) + Work(g.Action) * (_networks.Contains(g.Destination) ? NetworkFactor : 1) : 0;
            state.Groups.Add(new SWEmpireIntelligenceGroupView { Id = g.Id, Name = $"Группа {g.Id}", WorldId = g.World, WorldName = Name(g.World), DestinationId = g.Destination, DestinationName = Name(g.Destination), Readiness = g.Readiness, EtaHours = eta, LegStartedAt = g.Path.Count > 0 ? g.LegStartedAt : 0, LegEndsAt = g.Path.Count > 0 ? g.LegUntil : 0, NextWorldId = g.Path.Count > 0 ? g.Path.Peek() : "", NextWorldName = g.Path.Count > 0 ? Name(g.Path.Peek()) : "",
                Status = g.Path.Count > 0 ? "В пути" : g.Working ? "Выполняет задание" : Faction(g.World) == "empire" && g.Readiness < 100 ? "Отдых" : "Ожидает приказа", Route = path.Select(Name).ToList(), RouteWorldIds = path.ToList() });
        }
        state.Effects = _effects.Values.Select(e => new SWEmpireIntelligenceEffectView { WorldId = e.World, TargetName = e.Name, RemainingHours = Math.Max(0,e.Ends-_time.ActiveGameHours), Status = e.Weak ? "Ограниченное воздействие" : "Диверсия" }).ToList();
        return state;
    }
}





