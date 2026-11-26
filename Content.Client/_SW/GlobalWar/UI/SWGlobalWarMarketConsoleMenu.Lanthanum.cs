using System.Linq;
using Content.Shared._SW.GlobalWar;

namespace Content.Client._SW.GlobalWar.UI;

public sealed partial class SWGlobalWarMarketConsoleMenu
{
    public event Action<SWGlobalWarLanthanumCommand>? LanthanumRequested;

    private void InitializeLanthanum()
    {
        LanthanumToggle.OnPressed += _ => LanthanumBody.Visible = !LanthanumBody.Visible;
        LanthanumSave.OnPressed += _ => SendLanthanum();
        LanthanumLoad.OnPressed += _ =>
        {
            if (_state?.Lanthanum is not { } current || FlowDraft.PendingLanthanum != null)
                return;
            FlowDraft.LanthanumRevision = current.Revision;
            FlowDraft.LanthanumAutomatic = !current.Automatic;
            FlowDraft.LanthanumFeedback = string.Empty;
            RefreshLanthanum();
        };
        LanthanumReapply.OnPressed += _ =>
        {
            if (_state?.Lanthanum is not { } current || FlowDraft.PendingLanthanum != null)
                return;
            FlowDraft.LanthanumRevision = current.Revision;
            SendLanthanum();
        };
        LanthanumRetry.OnPressed += _ =>
        {
            if (FlowDraft.PendingLanthanum is { } pending)
                LanthanumRequested?.Invoke(pending);
        };
    }

    private void SendLanthanum()
    {
        if (_state is not { Head: true, Lanthanum: { } current } || FlowDraft.PendingLanthanum != null ||
            current.Revision != FlowDraft.LanthanumRevision || FlowDraft.LanthanumAutomatic is not { } automatic)
            return;
        FlowDraft.PendingLanthanum = new(_state.Generation, Guid.NewGuid(), _state.AssignmentRevision,
            FlowDraft.LanthanumRevision, automatic);
        FlowDraft.LanthanumFeedback = Loc.GetString("sw-global-war-economy-pending");
        LanthanumRequested?.Invoke(FlowDraft.PendingLanthanum);
        RefreshLanthanum();
    }

    public void ReceiveLanthanumResult(SWGlobalWarLanthanumResult result)
    {
        if (result.Request != FlowDraft.PendingLanthanum?.Request)
            return;
        var automatic = FlowDraft.PendingLanthanum.Automatic;
        FlowDraft.PendingLanthanum = null;
        if (result.Success)
        {
            FlowDraft.LanthanumRevision = result.Revision;
            FlowDraft.LanthanumAutomatic = !automatic;
        }
        FlowDraft.LanthanumFeedback = Loc.GetString(result.Success
            ? automatic ? "sw-global-war-ln-enabled" : "sw-global-war-ln-disabled"
            : result.Conflict ? "sw-global-war-ln-conflict" :
            string.IsNullOrEmpty(result.Reason) ? "sw-global-war-ln-denied" : result.Reason);
        RefreshLanthanum();
    }

    private void RefreshLanthanum()
    {
        if (_state == null)
            return;
        LanthanumPanel.Visible = _state.Lanthanum != null &&
            (_selected == null && _route == null && _commodity == null || _commodity?.Id == "SWGlobalWarLn");
        if (_state.Lanthanum is not { } view)
        {
            LanthanumBody.Visible = false;
            SetText(LanthanumCurrent, string.Empty);
            SetText(LanthanumExecution, string.Empty);
            SetText(LanthanumSupply, string.Empty);
            SetText(LanthanumDeposits, string.Empty);
            SetText(LanthanumResult, string.Empty);
            return;
        }
        foreach (var result in _state.LanthanumResults)
            if (result.Request == FlowDraft.PendingLanthanum?.Request)
                ReceiveLanthanumResult(result);
        if (FlowDraft.LanthanumAutomatic == null)
        {
            FlowDraft.LanthanumAutomatic = !view.Automatic;
            FlowDraft.LanthanumRevision = view.Revision;
        }
        var pending = FlowDraft.PendingLanthanum != null;
        var conflict = view.Revision != FlowDraft.LanthanumRevision;
        LanthanumSave.Visible = LanthanumRetry.Visible = _state.Head;
        LanthanumSave.Disabled = !_state.Head || pending || conflict;
        LanthanumSave.Text = Loc.GetString(FlowDraft.LanthanumAutomatic.Value ? "sw-global-war-ln-enable" : "sw-global-war-ln-disable");
        LanthanumLoad.Visible = LanthanumReapply.Visible = conflict && _state.Head;
        LanthanumLoad.Disabled = LanthanumReapply.Disabled = pending;
        LanthanumRetry.Disabled = !pending;
        SetText(LanthanumCurrent, Loc.GetString(view.Automatic ? "sw-global-war-ln-doctrine-auto" : "sw-global-war-ln-doctrine-off"));
        SetText(LanthanumExecution, view.Phase == SWGlobalWarLanthanumPhase.Starting
            ? Loc.GetString("sw-global-war-ln-starting", ("seconds", view.ReadyAt == null ? 0 :
                Math.Max(0, (int) Math.Ceiling((view.ReadyAt.Value - _state.Time).TotalSeconds))))
            : view.Phase == SWGlobalWarLanthanumPhase.Waiting
                ? Loc.GetString("sw-global-war-ln-wait-" + view.WaitingReason.ToString().ToLowerInvariant())
                : LanthanumPhase(view.Phase));
        SetText(LanthanumSupply, Loc.GetString("sw-global-war-ln-supply", ("stock", view.Stock.ToString("0.#####")),
            ("demand", view.Demand.ToString("0.#####")), ("threshold", (20 * view.Demand).ToString("0.#####")),
            ("electronics", view.CivilElectronics.ToString("0.#####")), ("t2", view.EmergencyT2.ToString("0.#####")),
            ("incoming", view.Incoming.ToString("0.#####"))));
        SetText(LanthanumDeposits, string.Join("\n", view.Deposits.Select(d => Loc.GetString("sw-global-war-ln-deposit",
            ("planet", PlanetName(d.Planet)), ("remaining", d.Remaining), ("released", d.Released), ("state", LanthanumPhase(d.Phase))))));
        SetText(LanthanumAuthority, Loc.GetString("sw-global-war-ln-head-only"));
        SetText(LanthanumIntent, _state.Head ? Loc.GetString(FlowDraft.LanthanumAutomatic.Value
            ? "sw-global-war-ln-enable-effect" : "sw-global-war-ln-disable-effect") : string.Empty);
        SetText(LanthanumConflict, conflict ? Loc.GetString("sw-global-war-ln-conflict") : string.Empty);
        SetText(LanthanumResult, FlowDraft.LanthanumFeedback);
    }

    private static string LanthanumPhase(SWGlobalWarLanthanumPhase phase) =>
        Loc.GetString("sw-global-war-ln-state-" + phase.ToString().ToLowerInvariant());
}
