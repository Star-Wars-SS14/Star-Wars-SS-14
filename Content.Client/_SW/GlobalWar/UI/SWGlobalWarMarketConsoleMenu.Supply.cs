using System.Linq;
using Content.Shared._SW.GlobalWar;

namespace Content.Client._SW.GlobalWar.UI;

public sealed partial class SWGlobalWarMarketConsoleMenu
{
    public event Action<SWGlobalWarSupplyCommand>? SupplyRequested;
    private bool _supplyRefreshing;
    private SWGlobalWarSupplyKind? _supplyRequestedKind;

    private void InitializeSupply()
    {
        foreach (var kind in Enum.GetValues<SWGlobalWarSupplyKind>())
            SupplyKindPicker.AddItem(SupplyName(kind), (int) kind);
        SupplyToggle.OnPressed += _ => SupplyBody.Visible = !SupplyBody.Visible;
        SupplyKindPicker.OnItemSelected += args =>
        {
            if (_supplyRefreshing)
                return;
            FlowDraft.SupplyKind = (SWGlobalWarSupplyKind) args.Id;
            FlowDraft.SupplyRevision = -1;
            _supplyRequestedKind = null;
            RefreshSupply();
        };
        SupplyLocal.OnTextChanged += _ =>
        {
            if (!_supplyRefreshing)
            {
                FlowDraft.SupplyLocal = SupplyLocal.Text;
                RefreshSupply();
            }
        };
        SupplyBacta.OnToggled += _ =>
        {
            if (!_supplyRefreshing)
                FlowDraft.SupplyBacta = SupplyBacta.Pressed;
        };
        SupplyHeadDecision.OnToggled += _ =>
        {
            if (!_supplyRefreshing)
            {
                FlowDraft.SupplyHead = SupplyHeadDecision.Pressed;
                RefreshSupply();
            }
        };
        SupplyLoad.OnPressed += _ =>
        {
            if (FlowDraft.PendingSupply == null)
            {
                FlowDraft.SupplyRevision = -1;
                RefreshSupply();
            }
        };
        SupplySave.OnPressed += _ =>
        {
            if (SupplySave.Disabled || _state is not { Supply.CanProduce: true } || !int.TryParse(FlowDraft.SupplyLocal, out var local) ||
                local is < 0 or > 100)
                return;
            SendSupply(new(_state.Generation, Guid.NewGuid(), _state.AssignmentRevision,
                SWGlobalWarSupplyAction.ConfigureLine, FlowDraft.SupplyKind, FlowDraft.SupplyRevision,
                LocalPercent: local, Bacta: FlowDraft.SupplyBacta, HeadDecision: FlowDraft.SupplyHead));
        };
        SupplyReapply.OnPressed += _ =>
        {
            if (SupplyReapply.Disabled || _state?.Supply is not { CanProduce: true } ||
                _state.Supply.Lines.FirstOrDefault(l => l.Kind == FlowDraft.SupplyKind) is not { } current)
                return;
            FlowDraft.SupplyRevision = current.Revision;
            RefreshSupply();
            if (int.TryParse(FlowDraft.SupplyLocal, out var local) && local is >= 0 and <= 100)
                SendSupply(new(_state.Generation, Guid.NewGuid(), _state.AssignmentRevision,
                    SWGlobalWarSupplyAction.ConfigureLine, FlowDraft.SupplyKind, current.Revision,
                    LocalPercent: local, Bacta: FlowDraft.SupplyBacta, HeadDecision: FlowDraft.SupplyHead));
        };
    }

    private void SendSupply(SWGlobalWarSupplyCommand command)
    {
        if (FlowDraft.PendingSupply != null)
            return;
        FlowDraft.PendingSupply = command;
        FlowDraft.SupplyFeedback = Loc.GetString("sw-global-war-economy-pending");
        SupplyRequested?.Invoke(command);
        RefreshSupply();
    }

    public void ReceiveSupplyResult(SWGlobalWarSupplyResult result)
    {
        if (result.Request != FlowDraft.PendingSupply?.Request)
            return;
        FlowDraft.PendingSupply = null;
        FlowDraft.SupplySeenResults[result.Request] = result;
        if (result.Success)
            FlowDraft.SupplyRevision = result.Revision;
        FlowDraft.SupplyFeedback = result.Success
            ? Loc.GetString("sw-global-war-supply-done")
            : SupplyFailure(result.Failure);
        RefreshSupply();
    }

    private void RefreshSupply()
    {
        if (_supplyRefreshing)
            return;
        _supplyRefreshing = true;
        try
        {
            SupplyPanel.Visible = _state?.Supply?.CanProduce == true;
            if (_state?.Supply is not { CanProduce: true } view)
                return;
            foreach (var result in _state.SupplyResults)
                if ((!FlowDraft.SupplySeenResults.TryGetValue(result.Request, out var seen) || result != seen) &&
                    result.Request == FlowDraft.PendingSupply?.Request)
                    ReceiveSupplyResult(result);
            var line = view.Lines.First(l => l.Kind == FlowDraft.SupplyKind);
            if (view.Kind != line.Kind && _supplyRequestedKind != line.Kind)
            {
                _supplyRequestedKind = line.Kind;
                PageRequested?.Invoke(_state.Query with { SupplyPage = 0, SupplyKind = line.Kind });
            }
            else if (view.Kind == line.Kind)
                _supplyRequestedKind = null;
            if (FlowDraft.SupplyRevision < 0)
            {
                FlowDraft.SupplyRevision = line.Revision;
                FlowDraft.SupplyLocal = line.LocalPercent.ToString();
                FlowDraft.SupplyBacta = line.Bacta;
            }
            SupplyKindPicker.SelectId((int) line.Kind);
            SupplyLocal.Text = FlowDraft.SupplyLocal;
            SupplyBacta.Pressed = FlowDraft.SupplyBacta;
            SupplyHeadDecision.Pressed = FlowDraft.SupplyHead;
            SupplyHeadDecision.Visible = _state.Head;
            var pending = FlowDraft.PendingSupply != null;
            SupplyKindPicker.Disabled = pending;
            SupplyLineControls.Visible = view.CanProduce;
            SupplyBacta.Visible = line.Kind == SWGlobalWarSupplyKind.Medicine;
            var validLocal = int.TryParse(FlowDraft.SupplyLocal, out var local) && local is >= 0 and <= 100;
            SupplySave.Disabled = pending || !validLocal || line.Revision != FlowDraft.SupplyRevision ||
                view.IndustryHeadDecisionRequired && !FlowDraft.SupplyHead;
            SupplyReapply.Visible = line.Revision != FlowDraft.SupplyRevision;
            SupplyReapply.Disabled = pending || !validLocal || view.IndustryHeadDecisionRequired && !FlowDraft.SupplyHead;
            SetText(SupplyLocalError, validLocal ? string.Empty : Loc.GetString("sw-global-war-supply-invalid-percent"));
            SetText(SupplyServerValues, Loc.GetString("sw-global-war-supply-server-values", ("local", line.LocalPercent),
                ("item", SupplyItem(line.Kind, line.Bacta))));
            SupplyLoad.Disabled = pending;
            SetText(SupplyConflict, line.Revision != FlowDraft.SupplyRevision ? Loc.GetString("sw-global-war-supply-conflict") : string.Empty);
            SetText(SupplyResult, FlowDraft.SupplyFeedback);
        }
        finally
        {
            _supplyRefreshing = false;
        }
    }

    private static string SupplyName(SWGlobalWarSupplyKind kind) => Loc.GetString("sw-global-war-supply-kind-" + kind.ToString().ToLowerInvariant());
    private static string SupplyItem(SWGlobalWarSupplyKind kind, bool bacta) => kind != SWGlobalWarSupplyKind.Medicine
        ? SupplyName(kind) : Loc.GetString(bacta ? "sw-global-war-supply-bacta-item" : "sw-global-war-supply-kolto-item");
    private static string SupplyStage(SWGlobalWarSupplyStage stage) => Loc.GetString("sw-global-war-supply-stage-" + stage.ToString().ToLowerInvariant());
    private static string SupplyFailure(SWGlobalWarSupplyFailure failure) => failure == SWGlobalWarSupplyFailure.None
        ? string.Empty : Loc.GetString("sw-global-war-supply-failure-" + failure.ToString().ToLowerInvariant());
}
