using System.Linq;
using Content.Shared._SW.GlobalWar.Politics;
using Robust.Server.GameObjects;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._SW.GlobalWar.Politics;

/// <summary>Only addressed BUI messages carry government data; there is no shared private BUI state.</summary>
public sealed class SWGlobalWarGovernmentConsoleSystem : EntitySystem
{
    [Dependency] private readonly SWGlobalWarPoliticsSystem _politics = default!;
    [Dependency] private readonly SWGlobalWarAssignmentsSystem _assignments = default!;
    [Dependency] private readonly SWGlobalWarLedgerSystem _ledger = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    private TimeSpan _nextUpdate;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWGlobalWarGovernmentConsoleComponent, BoundUIOpenedEvent>(OnOpened);
        SubscribeLocalEvent<SWGlobalWarGovernmentConsoleComponent, SWGlobalWarPoliticalRequest>(OnPoliticalRequest);
        SubscribeLocalEvent<SWGlobalWarGovernmentConsoleComponent, SWGlobalWarHistoryRequest>(OnHistoryRequest);
        SubscribeLocalEvent<SWGlobalWarGovernmentConsoleComponent, SWGlobalWarFinanceRequest>(OnFinanceRequest);
        SubscribeLocalEvent<SWGlobalWarGovernmentConsoleComponent, SWGlobalWarFinancePageRequest>(OnFinancePageRequest);
        SubscribeLocalEvent<SWGlobalWarAssignmentsChangedEvent>((ref SWGlobalWarAssignmentsChangedEvent _) => Refresh());
        SubscribeLocalEvent<SWGlobalWarPoliticsChangedEvent>((ref SWGlobalWarPoliticsChangedEvent _) => Refresh());
        SubscribeLocalEvent<SWGlobalWarLedgerChangedEvent>((ref SWGlobalWarLedgerChangedEvent _) => Refresh());
        SubscribeLocalEvent<SWGlobalWarCampaignStoppedEvent>((ref SWGlobalWarCampaignStoppedEvent _) => Refresh());
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _nextUpdate)
            return;
        _nextUpdate = _timing.CurTime + TimeSpan.FromSeconds(1);
        Refresh();
    }

    private void OnOpened(Entity<SWGlobalWarGovernmentConsoleComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (Equals(args.UiKey, SWGlobalWarGovernmentUiKey.Key))
            SendView(ent, args.Actor);
    }

    private void Refresh()
    {
        var query = EntityQueryEnumerator<SWGlobalWarGovernmentConsoleComponent, UserInterfaceComponent>();
        while (query.MoveNext(out var uid, out var console, out var ui))
            foreach (var actor in _ui.GetActors((uid, ui), SWGlobalWarGovernmentUiKey.Key).ToArray())
                SendView((uid, console), actor);
    }

    private void SendView(Entity<SWGlobalWarGovernmentConsoleComponent> ent, EntityUid actor)
    {
        var session = CompOrNull<ActorComponent>(actor)?.PlayerSession;
        if (ent.Comp.Kind == SWGlobalWarGovernmentConsoleKind.Finance)
        {
            _ui.ServerSendUiMessage(ent.Owner, SWGlobalWarGovernmentUiKey.Key,
                new SWGlobalWarFinanceSnapshot(_ledger.GetFinanceView(session, ent.Comp.Faction)), actor);
            return;
        }
        var view = _politics.GetDiplomacyView(session, ent.Comp.Faction);
        _ui.ServerSendUiMessage(ent.Owner, SWGlobalWarGovernmentUiKey.Key,
            new SWGlobalWarDiplomacySnapshot(view), actor);
    }

    private void OnPoliticalRequest(Entity<SWGlobalWarGovernmentConsoleComponent> ent, ref SWGlobalWarPoliticalRequest args)
    {
        var result = new SWGlobalWarPoliticalResultView(args.Request, false,
            "sw-global-war-politics-denied", Guid.Empty);
        if (ent.Comp.Kind == SWGlobalWarGovernmentConsoleKind.Diplomacy && TryComp<ActorComponent>(args.Actor, out var actor))
        {
            if (!_assignments.TryGetActor(actor.PlayerSession, args.Generation, args.AssignmentRevision, out var assignment))
                result = result with { Reason = "sw-global-war-politics-denied-assignment" };
            else if (assignment.Faction != ent.Comp.Faction)
                result = result with { Reason = "sw-global-war-politics-denied-faction" };
            else
            {
                var receipt = _politics.Execute(actor.PlayerSession, new SWGlobalWarPoliticalCommand(
                    args.Generation, args.Request, args.AssignmentRevision, args.Action, args.RelationsRevision,
                    args.Sector, args.Commodity, args.Enabled, args.Amount, args.Text, args.Confirmation));
                result = new SWGlobalWarPoliticalResultView(args.Request, receipt.Success, receipt.Reason, receipt.Confirmation, receipt.RetryAfter);
            }
        }
        _ui.ServerSendUiMessage(ent.Owner, SWGlobalWarGovernmentUiKey.Key, new SWGlobalWarPoliticalResult(result), args.Actor);
        SendView(ent, args.Actor);
    }

    private void OnHistoryRequest(Entity<SWGlobalWarGovernmentConsoleComponent> ent, ref SWGlobalWarHistoryRequest args)
    {
        if (ent.Comp.Kind != SWGlobalWarGovernmentConsoleKind.Diplomacy)
            return;
        var page = _politics.GetHistoryPage(CompOrNull<ActorComponent>(args.Actor)?.PlayerSession,
            ent.Comp.Faction, args.Generation, args.AssignmentRevision, args.Before);
        _ui.ServerSendUiMessage(ent.Owner, SWGlobalWarGovernmentUiKey.Key, page, args.Actor);
    }

    private void OnFinanceRequest(Entity<SWGlobalWarGovernmentConsoleComponent> ent, ref SWGlobalWarFinanceRequest args)
    {
        var accepted = false;
        var reservation = Guid.Empty;
        var effectAmount = 0L;
        var failure = SWGlobalWarFinanceFailure.Denied;
        if (ent.Comp.Kind == SWGlobalWarGovernmentConsoleKind.Finance && Enum.IsDefined(args.Action) &&
            TryComp<ActorComponent>(args.Actor, out var actor) &&
            _assignments.TryGetActor(actor.PlayerSession, args.Generation, args.AssignmentRevision, out var assignment) &&
            assignment.Faction == ent.Comp.Faction)
        {
            var action = args.Action switch
            {
                SWGlobalWarFinanceAction.Transfer => SWGlobalWarLedgerAction.Transfer,
                SWGlobalWarFinanceAction.Reserve => SWGlobalWarLedgerAction.Reserve,
                _ => SWGlobalWarLedgerAction.Release,
            };
            // ObservedRevision is deliberately stable across retries; the ledger checks current spendable balances.
            accepted = _ledger.TryExecute(actor.PlayerSession, args.AssignmentRevision,
                new SWGlobalWarLedgerCommand(args.Generation, args.Request, 0, action, assignment.Faction,
                    args.Account, args.Amount, args.Destination, args.Reservation, args.Employee, args.Purpose),
                out reservation, out failure);
            if (accepted && _ledger.GetOwnFinanceResult(actor.PlayerSession, args.Generation, args.Request) is { } receipt)
                effectAmount = receipt.EffectAmount;
        }
        _ui.ServerSendUiMessage(ent.Owner, SWGlobalWarGovernmentUiKey.Key,
            new SWGlobalWarFinanceResult(new SWGlobalWarFinanceResultView(args.Request, accepted, reservation,
                effectAmount, failure)), args.Actor);
        SendView(ent, args.Actor);
    }

    private void OnFinancePageRequest(Entity<SWGlobalWarGovernmentConsoleComponent> ent, ref SWGlobalWarFinancePageRequest args)
    {
        if (ent.Comp.Kind != SWGlobalWarGovernmentConsoleKind.Finance)
            return;
        var page = _ledger.GetFinancePage(CompOrNull<ActorComponent>(args.Actor)?.PlayerSession, ent.Comp.Faction, args);
        _ui.ServerSendUiMessage(ent.Owner, SWGlobalWarGovernmentUiKey.Key, page, args.Actor);
    }
}
