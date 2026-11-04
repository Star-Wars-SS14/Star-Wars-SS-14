using System.Linq;
using Content.Server._SW.GlobalWar.Access;
using Content.Server._SW.GlobalWar.Identity;
using Content.Server.Chat.Managers;
using Content.Server.GameTicking;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._SW.GlobalWar.Politics;

/// <summary>Server-owned diplomacy, neutral accession and government correspondence on the existing campaign.</summary>
public sealed partial class SWGlobalWarPoliticsSystem : EntitySystem
{
    [Dependency] private readonly SWGlobalWarCampaignSystem _campaign = default!;
    [Dependency] private readonly SWGlobalWarGameruleSystem _rules = default!;
    [Dependency] private readonly SWGlobalWarPlanetSystem _planets = default!;
    [Dependency] private readonly SWGlobalWarAssignmentsSystem _appointments = default!;
    [Dependency] private readonly SWGlobalWarIdentitySystem _identity = default!;
    [Dependency] private readonly SWGlobalWarLedgerSystem _ledger = default!;
    [Dependency] private readonly SWGlobalWarAccessPolicySystem _access = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly GameTicker _ticker = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly SWGlobalWarCampaignStepSystem _steps = default!;

    private bool _executing;
    private bool _outcomePending;
    private long _publishedRevision;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWGlobalWarCampaignStartedEvent>(OnStarted);
        SubscribeLocalEvent<SWGlobalWarCampaignStoppedEvent>(OnStopped);
        SubscribeLocalEvent<SWGlobalWarAssignmentsChangedEvent>(OnAssignmentsChanged);
        SubscribeLocalEvent<SWGlobalWarEvaluateAccessEvent>(OnEvaluateAccess);
        SubscribeLocalEvent<SWGlobalWarEvaluateAppointmentEvent>(OnEvaluateAppointment);
        SubscribeLocalEvent<SWGlobalWarShipOwnershipChangedEvent>(OnShipOwnershipChanged);
        InitializeRoundEnd();
    }

    /// <summary>Reads the current server state. Network views must select only fields authorized for their recipient.</summary>
    public bool TryGetPolitics(out Entity<SWGlobalWarPoliticsComponent> politics)
    {
        politics = default;
        if (!_campaign.TryGetCampaign(out var campaign) ||
            !TryComp<SWGlobalWarPoliticsComponent>(campaign.Owner, out var state) || state.Generation != campaign.Comp.Generation)
            return false;
        politics = (campaign.Owner, state);
        return true;
    }

    /// <summary>Government history is inherited by the current head; former incumbents cannot fetch it again.</summary>
    public IReadOnlyList<SWGlobalWarStateMessage> GetStateMessages(ICommonSession session, Guid generation, long assignmentRevision)
    {
        if (!TryGetPolitics(out var politics) || !IsHead(session, generation, assignmentRevision, out _))
            return Array.Empty<SWGlobalWarStateMessage>();
        return politics.Comp.StateMessages.ToArray();
    }

    /// <summary>Executes an explicit political intent. No authority is derived from a console, ID or client-supplied faction.</summary>
    public SWGlobalWarPoliticalReceipt Execute(ICommonSession session, SWGlobalWarPoliticalCommand command)
    {
        var denied = new SWGlobalWarPoliticalReceipt(command, false, "sw-global-war-politics-denied", Guid.Empty);
        if (!TryGetPolitics(out var politics) || politics.Comp.Generation != command.Generation ||
            command.Request == Guid.Empty || !Enum.IsDefined(command.Action))
            return denied;
        if (!_appointments.TryGetActor(session, command.Generation, command.AssignmentRevision, out var head))
            return denied with { Reason = "sw-global-war-politics-denied-assignment" };
        if (head.Role != SWGlobalWarRole.Head)
            return denied with { Reason = "sw-global-war-politics-denied-head" };
        var key = (session.UserId, command.Request);
        if (politics.Comp.Requests.TryGetValue(key, out var previous))
            return previous.Command == command ? previous : denied;
        if (_executing || _steps.IsClosing)
            return denied;
        using var moneyNotifications = _ledger.DeferNotifications();
        _executing = true;
        SWGlobalWarPoliticalReceipt result;
        var before = politics.Comp.Revision;
        try
        {
            Advance(politics);
            var success = Apply(politics, head, command, out var reason, out var confirmation);
            var retryAfter = reason == "sw-global-war-politics-message-rate-limit" &&
                             politics.Comp.LastMessage.TryGetValue(head.User, out var last)
                ? TimeSpan.FromTicks(Math.Max(0, (TimeSpan.FromSeconds(1) - (Now() - last)).Ticks)) : TimeSpan.Zero;
            result = new SWGlobalWarPoliticalReceipt(command, success, reason, confirmation, retryAfter);
            politics.Comp.Requests.Add(key, result);
        }
        finally
        {
            _executing = false;
        }
        if (politics.Comp.Revision != before)
            Notify(politics);
        return result;
    }

    private bool IsHead(ICommonSession session, Guid generation, long revision, out SWGlobalWarAssignment head)
    {
        return _appointments.TryGetActor(session, generation, revision, out head) && head.Role == SWGlobalWarRole.Head;
    }

    private void OnStarted(ref SWGlobalWarCampaignStartedEvent args)
    {
        var state = EnsureComp<SWGlobalWarPoliticsComponent>(args.Rule);
        if (state.Generation == args.Generation)
            return;
        state.Generation = args.Generation;
        state.Revision = 0;
        state.RelationsRevision = 0;
        state.Relations = SWGlobalWarRelations.Peace;
        state.TruceConsents.Clear();
        state.Sanctions.Clear();
        state.Neutrals.Clear();
        state.Requests.Clear();
        state.StateMessages.Clear();
        state.LastMessage.Clear();
        state.SurrenderConfirmations.Clear();
        state.Surrenders.Clear();
        state.AbsentSince.Clear();
        state.DoctrineChecked = false;
        state.LimitAnnounced = false;
        state.LimitScore = null;
        state.SurrenderTick = null;
        state.NextUpdate = TimeSpan.Zero;
        state.Outcome = SWGlobalWarOutcome.None;
        state.Withdrawals.Clear();
        state.WithdrawalHistory.Clear();
        state.ShipWithdrawals.Clear();
        _outcomePending = false;
        _publishedRevision = 0;
        var rule = Comp<SWGlobalWarGameruleComponent>(args.Rule);
        foreach (var (id, planet) in rule.PlanetEntities)
        {
            if (_prototypes.Index(id).InitialOwner == SWGlobalWarFaction.Neutral)
                state.Neutrals.Add(id, new SWGlobalWarNeutralState { Planet = planet });
        }
    }

    private void OnStopped(ref SWGlobalWarCampaignStoppedEvent args)
    {
        if (!TryComp<SWGlobalWarPoliticsComponent>(args.Rule, out var state) || state.Generation != args.Generation)
            return;
        state.TruceConsents.Clear();
        state.SurrenderConfirmations.Clear();
        state.Withdrawals.Clear();
        state.ShipWithdrawals.Clear();
        foreach (var neutral in state.Neutrals.Values)
            RetireCaptures(neutral);
        if (state.Outcome == SWGlobalWarOutcome.None)
            state.Outcome = SWGlobalWarOutcome.AdministrativeStop;
    }

    private void OnAssignmentsChanged(ref SWGlobalWarAssignmentsChangedEvent args)
    {
        if (!TryGetPolitics(out var politics) || politics.Comp.Generation != args.Generation)
            return;
        var changed = false;
        foreach (var (faction, assignment) in politics.Comp.TruceConsents.ToArray())
        {
            if (CurrentHead(faction)?.Id == assignment)
                continue;
            politics.Comp.TruceConsents.Remove(faction);
            changed = true;
        }
        foreach (var (faction, confirmation) in politics.Comp.SurrenderConfirmations.ToArray())
        {
            if (CurrentHead(faction)?.Id != confirmation.Assignment)
                politics.Comp.SurrenderConfirmations.Remove(faction);
        }
        if (changed)
        {
            politics.Comp.RelationsRevision++;
            politics.Comp.Revision++;
            if (!_executing)
                Notify(politics);
        }
    }

    private SWGlobalWarAssignment? CurrentHead(SWGlobalWarFaction faction)
    {
        if (!_appointments.TryGetRegistry(out var registry))
            return null;
        return registry.Comp.Assignments.Values.FirstOrDefault(a => a.Faction == faction && a.Role == SWGlobalWarRole.Head);
    }

    private static SWGlobalWarFaction Opponent(SWGlobalWarFaction faction)
    {
        return faction == SWGlobalWarFaction.Republic ? SWGlobalWarFaction.Empire : SWGlobalWarFaction.Republic;
    }

    private TimeSpan Now()
    {
        return _campaign.TryGetCampaign(out var campaign) ? _campaign.Elapsed(campaign) : TimeSpan.Zero;
    }

    private void Notify(Entity<SWGlobalWarPoliticsComponent> politics)
    {
        _publishedRevision = politics.Comp.Revision;
        _access.NotifyPolicyChanged(politics.Comp.Generation);
        var changed = new SWGlobalWarPoliticsChangedEvent(politics.Comp.Generation, politics.Comp.Revision);
        RaiseLocalEvent(ref changed);
    }
}
