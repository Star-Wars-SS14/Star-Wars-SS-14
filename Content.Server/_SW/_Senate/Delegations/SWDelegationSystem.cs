using System.Linq;
using Content.Server._SW._Senate.Core;
using Content.Server.Ghost.Roles.Events;
using Content.Server.Mind;
using Content.Shared.GameTicking;
using Content.Shared._SW._Senate.Planner;
using Content.Shared.Mobs;
using Content.Shared.Verbs;
using Content.Shared.Mobs.Components;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Random;

namespace Content.Server._SW._Senate.Delegations;

public sealed partial class SWDelegationSystem : EntitySystem
{
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly SWMinistrySystem _ministry = default!;
    [Dependency] private readonly SWMinistryFinanceSystem _finance = default!;
    [Dependency] private readonly SWMinistryHealthSystem _health = default!;
    [Dependency] private readonly MindSystem _mind = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    private sealed class Visit
    {
        public required int Id;
        public required bool Health;
        public long PlannerOfficeRevision;
        public int Episode;
        public int TargetEffective;
        public TaxObligation? TaxObligation;
        public HealthObligation? HealthObligation;
        public string WorldName = string.Empty;
        public required string Sector;
        public required string Good;
        public required int OriginalSurcharge;
        public required int OriginalEffective;
        public required SWDelegationProposal Proposal;
        public EntityUid? Spawner;
        public EntityUid? Body;
        public NetUserId? Owner;
        public bool Declined;
        public bool TermsValid = true;
        public float RemainingDeparture;
        public float RemainingVisit;
        public float RemainingOffer;
        public float RemainingGrace;
        public float Grace;
    }

    private Visit? _visit;
    private int _nextVisitId;
    private float _privacyRefresh;
    private readonly Dictionary<(string Sector, string Good), double> _cooldowns = new();
    private double _elapsed;
    private float _untilCheck;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRestart);
        SubscribeLocalEvent<SWDelegationMemberComponent, GhostRoleSpawnerUsedEvent>(OnSpawned);
        SubscribeLocalEvent<SWDelegationMemberComponent, PlayerAttachedEvent>(OnAttached);
        SubscribeLocalEvent<SWDelegationMemberComponent, GetVerbsEvent<AlternativeVerb>>(OnVerbs);
        SubscribeLocalEvent<MobStateComponent, SWGuestRequestAuthorizeEvent>(OnGuestAuthorize);
        SubscribeLocalEvent<MobStateComponent, SWGuestRequestRespondAuthorizeEvent>(OnGuestRespond);
        SubscribeLocalEvent<ActorComponent, GetVerbsEvent<AlternativeVerb>>(OnPlannerVerbs);
        SubscribeLocalEvent<SWGuestRequestValidateEvent>(OnGuestValidate);
        SubscribeLocalEvent<SWFinanceTaxSettingsChangedEvent>(OnTaxSettingsChanged);
        SubscribeLocalEvent<SWSenateOfficeHolderChangedEvent>(OnOfficeChanged);
        SubscribeLocalEvent<PlayerDetachedEvent>(OnPlayerDetached);
        SubscribeLocalEvent<SWHealthAidOrderedEvent>(OnAidOrdered);
        SubscribeLocalEvent<SWHealthAidAppliedEvent>(OnAidApplied);
    }

    private void OnRestart(RoundRestartCleanupEvent ev)
    {
        CloseVisit();
        var retired = EntityQueryEnumerator<SWDelegationMemberComponent>();
        while (retired.MoveNext(out var body, out _))
            RemComp<SWDelegationMemberComponent>(body);
        _cooldowns.Clear();
        _taxObligations.Clear();
        _healthObligations.Clear();
        _elapsed = 0;
        _privacyRefresh = 0;
        _untilCheck = 0;
    }

    public override void Update(float frameTime)
    {
        if (!_time.ModeActive || frameTime <= 0)
            return;
        _elapsed += frameTime;
        TickTaxObligations();
        TickHealthObligations();
        TickRetired(frameTime);
        _privacyRefresh -= frameTime;
        if (_privacyRefresh <= 0)
        {
            _privacyRefresh = 1f;
            Changed();
        }
        if (_visit is { } visit)
        {
            if (!visit.Proposal.Agreed && !visit.Declined)
            {
                var valid = CanAccept(visit);
                if (visit.TermsValid && !valid)
                {
                    visit.Proposal.InvalidateTerms();
                    Changed();
                }
                visit.TermsValid = valid;
            }
            if (!visit.Proposal.Agreed && visit.Proposal.MinisterConsent is { } consent)
            {
                var minister = _ministry.GetActiveHolderEntity(Ministry(visit));
                if (minister is not { } current || !TryComp<ActorComponent>(current, out var currentActor)
                    || currentActor.PlayerSession.UserId != consent)
                {
                    visit.Proposal.InvalidateMinister();
                    Changed();
                }
            }
            if (visit.Owner == null)
            {
                visit.RemainingOffer -= frameTime;
                if (visit.RemainingOffer <= 0)
                    CloseVisit();
            }
            else
            {
                visit.RemainingVisit -= frameTime;
                if (IsLiveOwner(visit))
                    visit.RemainingGrace = visit.Grace;
                else
                    visit.RemainingGrace -= frameTime;
                if (visit.Declined)
                    visit.RemainingDeparture -= frameTime;

                if (visit.RemainingGrace <= 0)
                    CloseVisit(abandoned: true);
                else if (visit.RemainingVisit <= 0 || (visit.Declined && visit.RemainingDeparture <= 0))
                    CloseVisit();
            }
        }

        if (_visit != null || !_time.DayActive)
            return;
        _untilCheck -= frameTime;
        if (_untilCheck > 0)
            return;
        if (!TryPoint(out var point, out var pointUid))
            return;
        _untilCheck = Math.Max(1f, point.CheckInterval);
        TryGenerate(pointUid, point);
    }

    private bool TryPoint(out SWDelegationPointComponent point, out EntityUid uid)
    {
        var query = EntityQueryEnumerator<SWDelegationPointComponent>();
        if (query.MoveNext(out uid, out var found))
        {
            point = found;
            return true;
        }
        point = default!;
        uid = default;
        return false;
    }

    private bool TryGenerate(EntityUid pointUid, SWDelegationPointComponent point, string? kind = null, string? sector = null, string? targetId = null)
    {
        var candidates = new List<(bool Health, string Sector, string Target, string Name)>();
        if (_ministry.IsHolderActive(SWMinistryFinanceSystem.FinanceMinistryId))
            foreach (var item in _finance.ProducedGoods)
                if (ReasonAvailable(item.Sector, "tax:" + item.Good)
                    && !_taxObligations.Any(o => !o.Finished && o.Sector == item.Sector && o.Good == item.Good)
                    && _finance.TryGetLowerCommodityTax(item.Good, out _, out _))
                    candidates.Add((false, item.Sector, item.Good, string.Empty));
        if (_health.PathogenActive && _ministry.IsHolderActive(SWMinistryHealthSystem.HealthMinistryId))
            foreach (var world in _health.InfectedWorlds())
                if (ReasonAvailable(world.Sector, "health:" + world.Id) && _health.CanOrderAid(world.Id)
                    && !_healthObligations.Any(o => !o.Finished && o.World == world.Id && o.Episode == _health.PathogenEpisode))
                    candidates.Add((true, world.Sector, world.Id, world.Name));
        if (kind != null)
            candidates.RemoveAll(c => (c.Health ? "health" : "tax") != kind || c.Sector != sector || c.Target != targetId);
        if (candidates.Count == 0 || PendingCount >= MaxPendingObligations || RetiredCount() >= MaxRetiredBodies)
            return false;
        var selected = _random.Pick(candidates);
        var surcharge = 0;
        var effective = 0;
        var target = 0;
        var targetEffective = 0;
        if (!selected.Health && (!_finance.TryGetCommodityTax(selected.Target, out surcharge, out effective)
            || !_finance.TryGetLowerCommodityTax(selected.Target, out target, out targetEffective)))
            return false;
        var spawner = Spawn("SWDelegationSpawner", Transform(pointUid).Coordinates);
        _visit = new Visit
        {
            Id = ++_nextVisitId,
            Health = selected.Health,
            Episode = selected.Health ? _health.PathogenEpisode : 0,
            TargetEffective = targetEffective,
            WorldName = selected.Name,
            Sector = selected.Sector,
            Good = selected.Target,
            OriginalSurcharge = surcharge,
            OriginalEffective = effective,
            Proposal = new SWDelegationProposal(target),
            Spawner = spawner,
            RemainingOffer = Math.Max(1f, point.OfferDuration),
            RemainingDeparture = Math.Max(1f, point.OfferDuration),
            RemainingVisit = Math.Max(1f, point.VisitDuration),
            RemainingGrace = Math.Max(1f, point.DisconnectGrace),
            Grace = Math.Max(1f, point.DisconnectGrace),
        };
        return true;
    }

    public bool TryAdminStart(string kind, string sector, string target)
    {
        return _time.ModeActive && _time.DayActive && _visit == null
            && (kind is "tax" or "health") && TryPoint(out var point, out var uid)
            && TryGenerate(uid, point, kind, sector, target);
    }

    public string AdminStatus()
    {
        if (_visit is not { } visit)
            return $"Активного представителя нет. Ожидающих обязательств: {PendingCount}.";
        var details = visit.Health ? $"эпизод {visit.Episode}, мир {visit.WorldName}"
            : $"товар {visit.Good}, доплата {visit.OriginalSurcharge}% → {visit.Proposal.TargetSurcharge}%, эффективная {visit.OriginalEffective}% → {visit.TargetEffective}%";
        return $"Визит {visit.Id}: сектор {visit.Sector}, {details}; " +
               $"владелец {(visit.Owner == null ? "ожидается" : "назначен")}, соглашение {(visit.Proposal.Agreed ? "заключено" : visit.Declined ? "отклонено" : "не заключено")}.";
    }

    private bool ReasonAvailable(string sector, string key) =>
        !_cooldowns.TryGetValue((sector, key), out var until) || until <= _elapsed;

    private static string Ministry(Visit visit) => visit.Health
        ? SWMinistryHealthSystem.HealthMinistryId : SWMinistryFinanceSystem.FinanceMinistryId;

    private void OnOfficeChanged(SWSenateOfficeHolderChangedEvent ev)
    {
        if (_visit is not { Declined: false } visit || ev.Owner != Ministry(visit))
            return;
        // Bind calendar requests to the actual appointment, even if the same player returns later.
        if (visit.PlannerOfficeRevision < long.MaxValue)
            visit.PlannerOfficeRevision++;
        if (!visit.Proposal.Agreed)
            visit.Proposal.InvalidateTerms();
        Changed();
    }

    private void OnPlayerDetached(PlayerDetachedEvent ev)
    {
        if (_visit is not { Declined: false } visit || visit.Proposal.Agreed
            || (visit.Owner != ev.Player.UserId && visit.Proposal.MinisterConsent != ev.Player.UserId))
            return;
        visit.Proposal.InvalidateTerms();
        Changed();
    }

    private void OnTaxSettingsChanged(SWFinanceTaxSettingsChangedEvent ev)
    {
        TickTaxObligations();
        if (_visit is not { Health: false, Declined: false } visit || visit.Proposal.Agreed
            || !_finance.TaxSettingAffectsCommodity(visit.Good, ev.Id, ev.Relief))
            return;
        visit.Proposal.InvalidateTerms();
        visit.TermsValid = CanAccept(visit);
        Changed();
    }

    private void OnSpawned(EntityUid uid, SWDelegationMemberComponent component, GhostRoleSpawnerUsedEvent ev)
    {
        if (_visit is { Owner: null } visit && visit.Spawner == ev.Spawner && ev.Spawned == uid)
            visit.Body = uid;
    }

    private void OnAttached(EntityUid uid, SWDelegationMemberComponent component, PlayerAttachedEvent ev)
    {
        if (component.Retired)
        {
            if (component.Owner == ev.Player.UserId)
                _eui.OpenEui(new SWDelegationEui(component.VisitId), ev.Player);
            return;
        }
        if (_visit is not { } visit || visit.Body != uid || (visit.Owner != null && visit.Owner != ev.Player.UserId))
            return;
        visit.Owner = ev.Player.UserId;
        component.Owner = ev.Player.UserId;
        component.VisitId = visit.Id;
        component.Grace = visit.Grace;
        component.RemainingGrace = visit.Grace;
        visit.Spawner = null;
        _eui.OpenEui(new SWDelegationEui(visit.Id), ev.Player);
        Changed();
    }

    private bool IsLiveOwner(Visit visit) => visit.Body is { } body && !Deleted(body)
        && TryComp<ActorComponent>(body, out var actor)
        && actor.PlayerSession.UserId == visit.Owner
        && actor.PlayerSession.Status == SessionStatus.InGame
        && actor.PlayerSession.AttachedEntity == body
        && TryComp<MobStateComponent>(body, out var mob) && mob.CurrentState == MobState.Alive;

    private const int MaxRetiredBodies = 16;

    private int RetiredCount()
    {
        var count = 0;
        var query = EntityQueryEnumerator<SWDelegationMemberComponent>();
        while (query.MoveNext(out _, out var component))
            if (component.Retired) count++;
        return count;
    }

    private bool IsRetiredOwner(EntityUid body, SWDelegationMemberComponent component) =>
        component.Retired && component.Owner is { } owner && !Deleted(body)
        && TryComp<ActorComponent>(body, out var actor)
        && actor.PlayerSession.UserId == owner && actor.PlayerSession.Status == SessionStatus.InGame
        && actor.PlayerSession.AttachedEntity == body
        && TryComp<MobStateComponent>(body, out var mob) && mob.CurrentState == MobState.Alive;

    private bool HasForeignController(EntityUid body, NetUserId? owner)
    {
        var foreignActor = TryComp<ActorComponent>(body, out var actor) && actor.PlayerSession.UserId != owner;
        var foreignMind = _mind.TryGetMind(body, out _, out var mind) && mind.UserId != null && mind.UserId != owner;
        return foreignActor || foreignMind;
    }

    private void TickRetired(float frameTime)
    {
        var query = EntityQueryEnumerator<SWDelegationMemberComponent>();
        while (query.MoveNext(out var body, out var component))
        {
            if (!component.Retired)
                continue;
            if (HasForeignController(body, component.Owner))
            {
                RemComp<SWDelegationMemberComponent>(body);
                continue;
            }
            if (IsRetiredOwner(body, component))
                component.RemainingGrace = component.Grace;
            else if ((component.RemainingGrace -= frameTime) <= 0)
                QueueDel(body);
        }
    }

    private void CloseVisit(bool abandoned = false, bool evacuated = false)
    {
        if (_visit is not { } visit)
            return;
        if (visit.Spawner is { } spawner && !Deleted(spawner))
            QueueDel(spawner);
        if (visit.Body is { } body && !Deleted(body) && TryComp<SWDelegationMemberComponent>(body, out var member))
        {
            if (HasForeignController(body, visit.Owner))
                RemComp<SWDelegationMemberComponent>(body);
            else if (abandoned || evacuated)
                QueueDel(body);
            else
            {
                member.Retired = true;
                member.Owner = visit.Owner;
                member.VisitId = visit.Id;
                member.Grace = visit.Grace;
                member.RemainingGrace = visit.RemainingGrace;
            }
        }
        if (TryPoint(out var point, out _))
            _cooldowns[(visit.Sector, (visit.Health ? "health:" : "tax:") + visit.Good)] = _elapsed + Math.Max(0, point.Cooldown);
        _visit = null;
        Changed();
    }
}
