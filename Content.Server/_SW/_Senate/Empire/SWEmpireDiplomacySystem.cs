using System.Linq;
using Content.Server._SW._Senate.Core;
using Content.Server._SW._Senate.Debate;
using Content.Shared._SW._Senate.Economy;
using Content.Shared._SW._Senate.Empire;
using Content.Shared._SW._Senate.Sectors;
using Content.Shared.GameTicking;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.Enums;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._SW._Senate.Empire;

/// <summary>Agreements are immutable after proposal; all durations use active Senate hours.</summary>
public sealed partial class SWEmpireDiplomacySystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly SWSenateStateSystem _senate = default!;
    [Dependency] private readonly SWSenateFloorSystem _floor = default!;
    [Dependency] private readonly SWEmpireCouncilBudgetSystem _budget = default!;
    private readonly List<SWEmpireDiplomacyAgreementView> _agreements = new();
    private readonly HashSet<string> _requests = new();
    private int _nextId;
    private const int MaxAgreements = 64;
    private SWGalacticMapPrototype Map => _proto.Index<SWGalacticMapPrototype>("SenateRepublic");

    public override void Initialize()
    {
        UpdatesBefore.Add(typeof(SWSenateTimeSystem));
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => { ResetCargo(); _agreements.Clear(); _requests.Clear(); _nextId = 0; });
    }

    private static SWEmpireDiplomacyRequest Copy(SWEmpireDiplomacyRequest r, bool includeRoute = true) => new() { RequestId=r.RequestId, Action=r.Action, Term=r.Term, Party=r.Party, Scope=r.Scope,
        TargetId=r.TargetId, Hours=r.Hours, Good=r.Good, Amount=r.Amount, SourceWorld=r.SourceWorld, DestinationWorld=r.DestinationWorld,
        RouteWorlds=includeRoute ? r.RouteWorlds?.ToList() ?? new() : new(), RouteLinks=includeRoute ? r.RouteLinks?.ToList() ?? new() : new() };
    private SWEmpireDiplomacyAgreementView Copy(SWEmpireDiplomacyAgreementView a) => new() { Id=a.Id, Cost=a.Cost, Terms=Copy(a.Terms), Status=a.Status,
        CreatedAt=a.CreatedAt, StartsAt=a.StartsAt, EndsAt=a.EndsAt, Reason=a.Reason };
    public SWEmpireDiplomacyState BuildState() => new() { NowHours=_time.ActiveGameHours, ClockRunning=_time.ModeActive && _time.DayActive,
        Agreements=_agreements.Select(Copy).ToList(), Cargo=BuildCargo() };
    public SWEmpireDiplomacyState BuildRepublicState() => new() { NowHours=_time.ActiveGameHours, ClockRunning=_time.ModeActive && _time.DayActive,
        Agreements=_agreements.Where(a=>a.Terms.Party==SWEmpireDiplomacyParty.Republic).Select(Copy).ToList(), Cargo=BuildRepublicCargo() };

    public bool TryQuote(SWEmpireDiplomacyRequest r, out SWEmpireDiplomacyQuote quote)
    {
        quote = new();
        if (r == null || !Enum.IsDefined(r.Action) || !Enum.IsDefined(r.Term) || !Enum.IsDefined(r.Party) || !Enum.IsDefined(r.Scope) ||
            string.IsNullOrWhiteSpace(r.RequestId) || r.RequestId.Length>128 || r.TargetId == null || r.TargetId.Length>128 ||
            r.Good == null || r.Good.Length>128 || r.SourceWorld == null || r.SourceWorld.Length>128 || r.DestinationWorld == null || r.DestinationWorld.Length>128 ||
            !double.IsFinite(r.Amount) || r.Amount<0 || r.Hours is not (6 or 12)) { quote.Reason="Некорректные условия"; return false; }
        quote.Request=Copy(r, false);
        // Supplied route arrays are never authoritative: every quote computes them anew.
        quote.Request.RouteWorlds.Clear(); quote.Request.RouteLinks.Clear();
        if (!_time.ModeActive || !_time.DayActive) { quote.Reason="Договоры доступны в активное время"; return false; }
        if (_requests.Contains(r.RequestId)) { quote.Reason="Запрос уже принят"; return false; }
        if (r.Action==SWEmpireDiplomacyAction.ShipCargo) return QuoteCargo(r,quote);
        if (string.IsNullOrWhiteSpace(r.TargetId)) { quote.Reason="Выберите объект соглашения"; return false; }
        if (_agreements.Count>=MaxAgreements) { quote.Reason="Достигнут предел договоров"; return false; }
        if (r.Term == SWEmpireDiplomacyTerm.Transit && r.Scope is not (SWEmpireDiplomacyScope.Link or SWEmpireDiplomacyScope.World) ||
            r.Term is SWEmpireDiplomacyTerm.Basing or SWEmpireDiplomacyTerm.Supply && r.Scope != SWEmpireDiplomacyScope.World)
        { quote.Reason="Выберите подходящий объект соглашения"; return false; }
        var faction=r.Party==SWEmpireDiplomacyParty.Republic ? "republic" : "neutral";
        bool MatchWorld(SWGalacticMapWorld w) => Map.GetWorldFaction(w)==faction;
        if (r.Term==SWEmpireDiplomacyTerm.Transit && r.Scope==SWEmpireDiplomacyScope.World)
        {
            if (!TryTransitCorridor(r.SourceWorld,r.DestinationWorld,faction,out var worlds,out var links,out var unnecessary))
            { quote.Reason=unnecessary ? "Транзит через выбранную сторону не требуется" : "Нет согласуемого коридора между выбранными мирами"; return false; }
            quote.Request.TargetId=r.DestinationWorld; quote.Request.RouteWorlds=worlds; quote.Request.RouteLinks=links;
            quote.Cost=500; quote.Allowed=true; quote.Reason=r.Party==SWEmpireDiplomacyParty.Republic ? "Требуется решение канцлера" : "Нейтральная сторона рассмотрит условия"; return true;
        }
        var valid = r.Scope switch {
            SWEmpireDiplomacyScope.World => Map.Worlds.Any(w=>w.Id==r.TargetId && MatchWorld(w)),
            SWEmpireDiplomacyScope.Sector => Map.Worlds.Any(w=>(w.Sector?.Id==r.TargetId || w.PoliticalSectorId==r.TargetId) && MatchWorld(w)),
            SWEmpireDiplomacyScope.Link => _proto.EnumeratePrototypes<SWTransitLinkPrototype>().Any(l=>l.ID==r.TargetId && CivilianPhysicalLink(l) && Map.Worlds.Any(w=>(w.Id==l.From || w.Id==l.To) && MatchWorld(w))),
            _ => false };
        if (!valid) { quote.Reason="Объект не принадлежит выбранной стороне или переход недоступен"; return false; }
        if (r.Term==SWEmpireDiplomacyTerm.Transit && r.Scope==SWEmpireDiplomacyScope.Link)
        {
            var link=_proto.EnumeratePrototypes<SWTransitLinkPrototype>().First(l=>l.ID==r.TargetId);
            if (Map.Worlds.Where(w=>w.Id==link.From || w.Id==link.To).Any(w=>Map.GetWorldFaction(w)!="empire" && Map.GetWorldFaction(w)!=faction) ||
                !Map.Worlds.Any(w=>w.Id==link.From) || !Map.Worlds.Any(w=>w.Id==link.To))
            { quote.Reason="Для другой стороны необходимо отдельное соглашение"; return false; }
            quote.Request.RouteWorlds=new() { link.From,link.To }; quote.Request.RouteLinks=new() { link.ID };
        }
        if (r.Term==SWEmpireDiplomacyTerm.Basing && !_proto.EnumeratePrototypes<SWTransitLinkPrototype>().Any(l => l.Accessible &&
                (l.From==r.TargetId && Map.Worlds.Any(w=>w.Id==l.To && Map.GetWorldFaction(w)=="empire") ||
                 l.To==r.TargetId && Map.Worlds.Any(w=>w.Id==l.From && Map.GetWorldFaction(w)=="empire"))))
        { quote.Reason="Нет доступного мирного перехода от имперского мира"; return false; }
        if (r.Term==SWEmpireDiplomacyTerm.Supply) return QuoteSupply(r, quote);
        quote.Cost=500; quote.Allowed=true; quote.Reason=r.Party==SWEmpireDiplomacyParty.Republic ? "Требуется решение канцлера" : "Нейтральная сторона рассмотрит условия"; return true;
    }
    public bool TrySubmit(SWEmpireDiplomacyRequest request, out string reason)
    {
        if (!TryQuote(request,out var quote)) { reason=quote.Reason; return false; }
        if (request.Action==SWEmpireDiplomacyAction.ShipCargo) return SubmitCargo(request,out reason);
        if (!_budget.HasReservation(SWEmpireCouncilSphere.ExpansionDiplomacy,request.RequestId,quote.Cost)) { reason="Нет резерва средств сферы"; return false; }
        _requests.Add(request.RequestId);
        _agreements.Add(new SWEmpireDiplomacyAgreementView {Id=++_nextId, Cost=quote.Cost, Terms=Copy(quote.Request), CreatedAt=_time.ActiveGameHours,
            Status=request.Party==SWEmpireDiplomacyParty.Republic ? SWEmpireDiplomacyStatus.AwaitingApproval : SWEmpireDiplomacyStatus.Negotiating,
            Reason=request.Party==SWEmpireDiplomacyParty.Republic ? "Ожидается решение канцлера" : "Рассмотрение нейтральной стороной"});
        reason=""; return true;
    }
    public bool CanDecide(EntityUid actor) => _time.ModeActive && _time.DayActive && _senate.Chancellor != null && _floor.Chancellor==actor &&
        _senate.Chancellor==Name(actor) && TryComp<ActorComponent>(actor,out var player) && player.PlayerSession.Status==SessionStatus.InGame &&
        player.PlayerSession.AttachedEntity==actor && TryComp<MobStateComponent>(actor,out var mob) && mob.CurrentState==MobState.Alive;
    public bool Decide(int id, bool approve, EntityUid actor)
    {
        if (!CanDecide(actor)) return false;
        var a=_agreements.FirstOrDefault(x=>x.Id==id && x.Terms.Party==SWEmpireDiplomacyParty.Republic && x.Status==SWEmpireDiplomacyStatus.AwaitingApproval);
        if (a==null) return false;
        if (!approve) { a.Status=SWEmpireDiplomacyStatus.Rejected; a.Reason="Отклонено канцлером"; RefundSupply(a); return true; }
        Activate(a,"Одобрено канцлером"); return true;
    }
    public bool TryBreak(int id, out string reason)
    {
        var a=_agreements.FirstOrDefault(x=>x.Id==id && x.Status==SWEmpireDiplomacyStatus.Active);
        if (!_time.ModeActive || !_time.DayActive || a==null) { reason="Действующее соглашение не найдено"; return false; }
        a.Status=SWEmpireDiplomacyStatus.Broken; a.Reason="Соглашение прекращено"; StopSupply(a); reason=""; return true;
    }
    public bool BreakRepublic(int id, EntityUid actor) => CanDecide(actor) && _agreements.Any(x=>x.Id==id && x.Terms.Party==SWEmpireDiplomacyParty.Republic) && TryBreak(id,out _);
    private void Activate(SWEmpireDiplomacyAgreementView a,string reason) { a.Status=SWEmpireDiplomacyStatus.Active; a.StartsAt=_time.ActiveGameHours; a.EndsAt=a.StartsAt+a.Terms.Hours; a.Reason=reason; }
    public override void Update(float frameTime)
    {
        if (!_time.ModeActive || !_time.DayActive) return;
        TickCargo();
        foreach (var a in _agreements)
        {
            if (a.Status==SWEmpireDiplomacyStatus.Negotiating && _time.ActiveGameHours-a.CreatedAt>=1) Activate(a,"Условия приняты");
            else if (a.Status==SWEmpireDiplomacyStatus.AwaitingApproval && (_floor.Chancellor is not { } incumbent || !CanDecide(incumbent)) && _time.ActiveGameHours-a.CreatedAt>=1 &&
                a.Terms.Term is SWEmpireDiplomacyTerm.Transit or SWEmpireDiplomacyTerm.Supply) Activate(a,"Гражданские условия одобрены автопилотом");
            else if (a.Status==SWEmpireDiplomacyStatus.Active && _time.ActiveGameHours>=a.EndsAt) { a.Status=SWEmpireDiplomacyStatus.Expired; a.Reason="Срок истёк"; StopSupply(a); }
        }
    }
    private bool Active(SWEmpireDiplomacyAgreementView a) => _time.ModeActive && a.Status==SWEmpireDiplomacyStatus.Active && _time.ActiveGameHours<a.EndsAt;
    private bool TryTransitCorridor(string from, string to, string party, out List<string> worlds, out List<string> edges, out bool unnecessary)
    {
        worlds=new(); edges=new(); unnecessary=false;
        var byId=Map.Worlds.ToDictionary(w=>w.Id);
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to) || from==to ||
            !byId.TryGetValue(from,out var source) || !byId.ContainsKey(to) ||
            Map.GetWorldFaction(source) != "empire" && Map.GetWorldFaction(source) != party) return false;
        var links=_proto.EnumeratePrototypes<SWTransitLinkPrototype>()
            .Where(CivilianPhysicalLink).OrderBy(l=>l.ID).ToArray();
        var previous=new Dictionary<string,(string Parent,string Link)> { [from]=(string.Empty,string.Empty) };
        var pending=new Queue<string>(); pending.Enqueue(from);
        while (pending.Count>0 && !previous.ContainsKey(to))
        {
            var current=pending.Dequeue();
            foreach (var link in links)
            {
                var next=link.From==current ? link.To : link.To==current ? link.From : string.Empty;
                if (next.Length==0 || previous.ContainsKey(next) || !byId.TryGetValue(next,out var world)) continue;
                var nextFaction=Map.GetWorldFaction(world);
                if (nextFaction!="empire" && nextFaction!=party) continue;
                previous[next]=(current,link.ID); pending.Enqueue(next);
            }
        }
        if (!previous.ContainsKey(to)) return false;
        for (var current=to; current!=from; current=previous[current].Parent)
        { worlds.Add(current); edges.Add(previous[current].Link); }
        worlds.Add(from); worlds.Reverse(); edges.Reverse();
        if (edges.Count>64) return false;
        if (worlds.Any(world => Map.GetWorldFaction(byId[world])==party)) return true;
        unnecessary=true; worlds.Clear(); edges.Clear(); return false;
    }
    public bool BlocksHostileAction(string worldId, out string reason)
    {
        var world=Map.Worlds.FirstOrDefault(w=>w.Id==worldId);
        if (world!=null && _agreements.Any(a=>Active(a) && a.Terms.Term==SWEmpireDiplomacyTerm.LocalCeasefire &&
            (a.Terms.Scope==SWEmpireDiplomacyScope.World && a.Terms.TargetId==worldId || a.Terms.Scope==SWEmpireDiplomacyScope.Sector && (world.Sector?.Id==a.Terms.TargetId || world.PoliticalSectorId==a.Terms.TargetId))))
        { reason="Действует локальное перемирие"; return true; }
        reason=""; return false;
    }
    public bool BlocksHostileTransit(string linkId, out string reason)
    {
        if (_agreements.Any(a=>Active(a) && a.Terms.Term==SWEmpireDiplomacyTerm.LocalCeasefire && a.Terms.Scope==SWEmpireDiplomacyScope.Link && a.Terms.TargetId==linkId))
        { reason="На переходе действует перемирие"; return true; }
        var link = _proto.EnumeratePrototypes<SWTransitLinkPrototype>().FirstOrDefault(l => l.ID == linkId);
        if (link != null && (BlocksHostileAction(link.From, out reason) || BlocksHostileAction(link.To, out reason))) return true;
        reason=""; return false;
    }
    public bool AllowsCivilianTransit(string linkId) => _agreements.Any(a=>Active(a) && a.Terms.Term==SWEmpireDiplomacyTerm.Transit && a.Terms.RouteLinks.Contains(linkId));
    public bool AllowsCivilianBasing(string worldId) => _agreements.Any(a=>Active(a) && a.Terms.Term==SWEmpireDiplomacyTerm.Basing && a.Terms.TargetId==worldId);
}


