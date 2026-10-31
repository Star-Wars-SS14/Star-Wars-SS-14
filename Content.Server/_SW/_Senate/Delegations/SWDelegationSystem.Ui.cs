using Content.Server.EUI;
using Content.Server._SW._Senate.Core;
using Content.Shared._SW._Senate.Delegations;
using Content.Shared._SW._Senate.Sectors;
using Content.Shared.Eui;
using Content.Shared.Verbs;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._SW._Senate.Delegations;

public sealed partial class SWDelegationSystem
{
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly EuiManager _eui = default!;
    public event Action? Updated;
    private void Changed() => Updated?.Invoke();

    private void OnVerbs(EntityUid uid, SWDelegationMemberComponent component, GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess)
            return;
        if (component.Retired)
        {
            if (args.User != uid || !IsRetiredOwner(uid, component))
                return;
            args.Verbs.Add(new AlternativeVerb
            {
                Text = "Эвакуация представителя",
                Act = () =>
                {
                    if (IsRetiredOwner(uid, component) && TryComp<ActorComponent>(uid, out var retiredActor))
                        _eui.OpenEui(new SWDelegationEui(component.VisitId), retiredActor.PlayerSession);
                },
            });
            return;
        }
        if (_visit is not { } visit || visit.Body != uid || !IsLiveOwner(visit))
            return;
        if (!TryViewer(args.User, visit, out _))
            return;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = "Представительство",
            Act = () =>
            {
                if (TryComp<ActorComponent>(args.User, out var actor) && TryViewer(args.User, visit, out _))
                    _eui.OpenEui(new SWDelegationEui(visit.Id), actor.PlayerSession);
            },
        });
    }

    private bool TryViewer(EntityUid actor, Visit visit, out bool owner)
    {
        owner = false;
        if (!IsLiveOwner(visit) || !TryComp<ActorComponent>(actor, out var session)
            || session.PlayerSession.AttachedEntity != actor)
            return false;
        owner = actor == visit.Body && session.PlayerSession.UserId == visit.Owner;
        if (owner)
            return true;
        return _ministry.IsActiveHolder(Ministry(visit), actor)
            && visit.Body is { } body && _transform.InRange(actor, body, 3f);
    }

    private bool IsSessionActor(ICommonSession player, EntityUid actor) =>
        TryComp<ActorComponent>(actor, out var component) && component.PlayerSession.UserId == player.UserId;

    public SWDelegationEuiState BuildState(ICommonSession player, int visitId)
    {
        var state = new SWDelegationEuiState { Text = "Представительство недоступно." };
        if (player.AttachedEntity is { } retiredBody && IsSessionActor(player, retiredBody)
            && TryComp<SWDelegationMemberComponent>(retiredBody, out var retired) && retired.VisitId == visitId
            && retired.Owner == player.UserId && IsRetiredOwner(retiredBody, retired))
        {
            state.Text = "Визит завершён. Вернитесь к указателю у места прибытия для личной эвакуации.";
            state.CanEvacuate = TryPoint(out var point, out var pointUid) && _transform.InRange(retiredBody, pointUid, point.Range);
            return state;
        }
        if (player.AttachedEntity is not { } actor || _visit is not { } visit || visit.Id != visitId || !IsSessionActor(player, actor) || !TryViewer(actor, visit, out var owner))
            return state;
        var sector = _proto.TryIndex<SWSectorPrototype>(visit.Sector, out var sectorProto) ? sectorProto.Name : visit.Sector;
        state.Version = visit.Proposal.Version;
        if (visit.Health)
            state.Text = $"Сектор: {sector}\nМир: {visit.WorldName}\nПоручение: общая медицинская помощь миру во время текущей эпидемии.\n";
        else
        {
            _finance.TryGetCommodityName(visit.Good, out var good);
            state.Text = $"Сектор: {sector}\nТовар: {good}\nДополнительная ставка: {visit.OriginalSurcharge}% → {visit.Proposal.TargetSurcharge}%.\n" +
                         $"Фактическая ставка: {visit.OriginalEffective}% → {visit.TargetEffective}%.\n";
        }
        if (visit.Declined)
            state.Text += "Предложение отклонено. Обязательство не возникло.";
        else if (visit.Proposal.Agreed)
        {
            var finished = visit.Health ? visit.HealthObligation?.Finished == true : visit.TaxObligation?.Finished == true;
            var succeeded = visit.Health ? visit.HealthObligation?.Succeeded == true : visit.TaxObligation?.Succeeded == true;
            state.Text += succeeded ? "Обязательство исполнено." : finished
                ? (visit.Health && visit.HealthObligation?.EpidemicEnded == true ? "Эпидемия завершилась или сменилась." : "Срок обязательства истёк.")
                : "Соглашение заключено. Ожидается исполнение.";
        }
        else
        {
            state.Text += $"Подтверждение представителя: {(visit.Proposal.DelegateConsent != null ? "да" : "нет")}. " +
                          $"Подтверждение министра: {(visit.Proposal.MinisterConsent != null ? "да" : "нет")}.";
            if (!CanAccept(visit)) state.Text += " Условия изменились или лимит обязательств исчерпан.";
        }
        state.CanAccept = !visit.Declined && !visit.Proposal.Agreed && CanAccept(visit);
        state.CanDecline = !visit.Declined && !visit.Proposal.Agreed;
        if (owner)
        {
            state.Text += $"\nВремя визита: {Math.Ceiling(Math.Max(0, visit.RemainingVisit) / 60f):0} мин. " +
                          "Эвакуация: вернитесь к указателю у места прибытия.";
            state.CanEvacuate = TryPoint(out var point, out var pointUid) && visit.Body is { } body
                && _transform.InRange(body, pointUid, point.Range);
        }
        return state;
    }

    public void HandleAction(ICommonSession player, int visitId, SWDelegationActionMessage message)
    {
        if (player.AttachedEntity is { } retiredBody && IsSessionActor(player, retiredBody)
            && TryComp<SWDelegationMemberComponent>(retiredBody, out var retired) && retired.VisitId == visitId
            && retired.Owner == player.UserId && IsRetiredOwner(retiredBody, retired))
        {
            if (message.Action == SWDelegationAction.Evacuate && TryPoint(out var retiredPoint, out var retiredPointUid)
                && _transform.InRange(retiredBody, retiredPointUid, retiredPoint.Range))
            {
                RemComp<SWDelegationMemberComponent>(retiredBody);
                QueueDel(retiredBody);
                Changed();
            }
            return;
        }
        if (player.AttachedEntity is not { } actor || _visit is not { } visit || visit.Id != visitId || !IsSessionActor(player, actor) || !TryViewer(actor, visit, out var owner))
            return;
        if (message.Action == SWDelegationAction.Evacuate)
        {
            if (!owner || !TryPoint(out var point, out var pointUid) || visit.Body is not { } body
                || !_transform.InRange(body, pointUid, point.Range))
                return;
            CloseVisit(evacuated: true);
            Changed();
            return;
        }
        if (visit.Declined || visit.Proposal.Agreed || message.Version != visit.Proposal.Version)
            return;
        if (message.Action == SWDelegationAction.Decline)
        {
            visit.Declined = true;
            Changed();
            return;
        }
        if (message.Action != SWDelegationAction.Accept || !TryPoint(out var settings, out _)
            || !CanAccept(visit))
            return;
        var minister = _ministry.GetActiveHolderEntity(Ministry(visit));
        if (minister is not { } ministerBody || visit.Body is not { } delegateBody
            || !_transform.InRange(ministerBody, delegateBody, 3f)
            || !TryComp<ActorComponent>(ministerBody, out var ministerActor))
            return;
        if (owner)
            visit.Proposal.AcceptDelegate(player.UserId, message.Version);
        else
            visit.Proposal.AcceptMinister(player.UserId, message.Version);
        if (visit.Owner is { } ownerId && visit.Proposal.TryConclude(ownerId, ministerActor.PlayerSession.UserId, _elapsed, settings.ObligationDuration))
        {
            if (visit.Health) AddHealthObligation(visit);
            else AddTaxObligation(visit);
        }
        Changed();
    }
}

public sealed class SWDelegationEui : BaseEui
{
    [Dependency] private readonly IEntityManager _entity = default!;
    private SWDelegationSystem System => _entity.System<SWDelegationSystem>();
    private readonly int _visitId;
    public SWDelegationEui(int visitId) { _visitId = visitId; IoCManager.InjectDependencies(this); }
    public override void Opened() { base.Opened(); System.Updated += OnUpdated; StateDirty(); }
    public override void Closed() { base.Closed(); System.Updated -= OnUpdated; }
    private void OnUpdated() => StateDirty();
    public override EuiStateBase GetNewState() => System.BuildState(Player, _visitId);
    public override void HandleMessage(EuiMessageBase message)
    {
        base.HandleMessage(message);
        if (message is SWDelegationActionMessage action)
            System.HandleAction(Player, _visitId, action);
    }
}
