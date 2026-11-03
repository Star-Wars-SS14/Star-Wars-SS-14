using System.Globalization;
using System.Linq;
using Content.Shared._SW.GlobalWar;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._SW.GlobalWar.UI;

public sealed partial class SWGlobalWarMarketConsoleMenu
{
    public event Action<SWGlobalWarGrowthCommand>? GrowthRequested;
    private string[] _growthWorlds = [];
    private string[] _growthGrantIds = [];
    private string[] _growthWorkerIds = [];
    private (Guid, Guid, long)? _growthScope;

    private void InitializeGrowth()
    {
        GrowthTab.OnPressed += _ =>
        {
            GoodsScroll.Visible = FreightScroll.Visible = FlowsScroll.Visible = false;
            GrowthScroll.Visible = true;
            GoodsTab.SetAppearance(SWGlobalWarConsoleStyle.TextBright, selected: false, tab: true);
            FreightTab.SetAppearance(SWGlobalWarConsoleStyle.TextBright, selected: false, tab: true);
            FlowsTab.SetAppearance(SWGlobalWarConsoleStyle.TextBright, selected: false, tab: true);
            GrowthTab.SetAppearance(SWGlobalWarConsoleStyle.TextBright, selected: true, tab: true);
        };
        GrowthPercent.OnTextChanged += _ => { FlowDraft.GrowthPercent = GrowthPercent.Text; RefreshGrowth(); };
        GrowthMinimum.OnTextChanged += _ => FlowDraft.GrowthMinimum = GrowthMinimum.Text;
        GrowthMaximum.OnTextChanged += _ => FlowDraft.GrowthMaximum = GrowthMaximum.Text;
        GrowthMinutes.OnTextChanged += _ => FlowDraft.GrowthMinutes = GrowthMinutes.Text;
        GrowthHead.OnToggled += _ => { FlowDraft.GrowthHead = GrowthHead.Pressed; RefreshGrowth(); };
        GrowthWorld.OnItemSelected += args =>
        {
            if (FlowDraft.PendingGrowth != null)
                return;
            GrowthWorld.SelectId(args.Id);
            FlowDraft.GrowthPlanet = Selected(_growthWorlds, GrowthWorld);
            FlowDraft.GrowthFeedback = string.Empty;
            LoadGrowth();
        };
        GrowthGrantPicker.OnItemSelected += args =>
        {
            GrowthGrantPicker.SelectId(args.Id);
            FlowDraft.GrowthGrant = Guid.TryParse(Selected(_growthGrantIds, GrowthGrantPicker), out var id) ? id : Guid.Empty;
            RefreshGrowth();
        };
        GrowthWorker.OnItemSelected += args =>
        {
            GrowthWorker.SelectId(args.Id);
            var key = Selected(_growthWorkerIds, GrowthWorker);
            FlowDraft.GrowthWorker = _state?.FlowWorkers.FirstOrDefault(w => WorkerKey(w) == key);
        };
        GrowthLoad.OnPressed += _ => LoadGrowth();
        GrowthReapply.OnPressed += _ =>
        {
            if (FlowDraft.PendingGrowth != null || _state?.Growth.FirstOrDefault(g => g.Planet == FlowDraft.GrowthPlanet) is not { } current)
                return;
            FlowDraft.GrowthRevision = current.Revision;
            SendGrowth(SWGlobalWarGrowthAction.Set);
        };
        GrowthSave.OnPressed += _ => SendGrowth(SWGlobalWarGrowthAction.Set);
        GrowthGrant.OnPressed += _ => SendGrowth(SWGlobalWarGrowthAction.Grant);
        GrowthRevoke.OnPressed += _ => SendGrowth(SWGlobalWarGrowthAction.Revoke);
        GrowthRetry.OnPressed += _ =>
        {
            if (FlowDraft.PendingGrowth is { } pending)
                GrowthRequested?.Invoke(pending);
        };
    }

    private static string WorkerKey(SWGlobalWarFlowWorkerView worker) => $"{worker.User}/{worker.Revision}";

    private void LoadGrowth()
    {
        if (FlowDraft.PendingGrowth != null || _state?.Growth.FirstOrDefault(g => g.Planet == FlowDraft.GrowthPlanet) is not { } current)
            return;
        FlowDraft.GrowthRevision = current.Revision;
        GrowthPercent.Text = FlowDraft.GrowthPercent = current.Percent.ToString(CultureInfo.InvariantCulture);
        FlowDraft.GrowthFeedback = string.Empty;
        RefreshGrowth();
    }

    private void SendGrowth(SWGlobalWarGrowthAction action)
    {
        if (_state == null || FlowDraft.PendingGrowth != null || FlowDraft.GrowthPlanet is not { } planet)
            return;
        var percent = 10;
        var minimum = 0;
        var maximum = 25;
        var minutes = 10;
        if (action == SWGlobalWarGrowthAction.Set &&
                (!int.TryParse(GrowthPercent.Text, out percent) || percent is < 0 or > 25) ||
            action == SWGlobalWarGrowthAction.Grant &&
                (!int.TryParse(GrowthMinimum.Text, out minimum) || minimum < 0 ||
                 !int.TryParse(GrowthMaximum.Text, out maximum) || maximum > 25 || maximum < minimum ||
                 !int.TryParse(GrowthMinutes.Text, out minutes) || minutes <= 0 || FlowDraft.GrowthWorker == null) ||
            action == SWGlobalWarGrowthAction.Revoke && FlowDraft.GrowthGrant == Guid.Empty)
        {
            FlowDraft.GrowthFeedback = Loc.GetString("sw-global-war-growth-invalid");
            RefreshGrowth();
            return;
        }
        FlowDraft.PendingGrowth = new(_state.Generation, Guid.NewGuid(), _state.AssignmentRevision, action, planet,
            FlowDraft.GrowthRevision, percent, FlowDraft.GrowthGrant, FlowDraft.GrowthWorker?.User,
            FlowDraft.GrowthWorker?.Revision ?? 0, minimum, maximum, minutes, GrowthHead.Pressed);
        FlowDraft.GrowthFeedback = Loc.GetString("sw-global-war-economy-pending");
        GrowthRequested?.Invoke(FlowDraft.PendingGrowth);
        RefreshGrowth();
    }

    public void ReceiveGrowthResult(SWGlobalWarGrowthResult result)
    {
        if (result.Request != FlowDraft.PendingGrowth?.Request)
            return;
        var planet = FlowDraft.PendingGrowth.Planet;
        FlowDraft.PendingGrowth = null;
        if (result.Success && result.Action == SWGlobalWarGrowthAction.Set && result.View is { } view)
            FlowDraft.GrowthRevision = view.Revision;
        var feedback = result.Success ? Loc.GetString(result.Action switch
        {
            SWGlobalWarGrowthAction.Grant => "sw-global-war-economy-granted",
            SWGlobalWarGrowthAction.Revoke => "sw-global-war-economy-revoked",
            _ => "sw-global-war-growth-saved",
        }, ("percent", result.View?.Percent ?? 0)) : GrowthFailureText(result.Failure);
        FlowDraft.GrowthFeedback = Loc.GetString("sw-global-war-growth-result-world", ("planet", PlanetName(planet)), ("result", feedback));
        RefreshGrowth();
    }

    private void RefreshGrowth()
    {
        if (_state == null)
            return;
        var scope = (_state.Generation, _state.Assignment, _state.AssignmentRevision);
        if (_growthScope != scope)
        {
            _growthScope = scope;
            GrowthPercent.Text = FlowDraft.GrowthPercent;
            GrowthMinimum.Text = FlowDraft.GrowthMinimum;
            GrowthMaximum.Text = FlowDraft.GrowthMaximum;
            GrowthMinutes.Text = FlowDraft.GrowthMinutes;
            GrowthHead.Pressed = FlowDraft.GrowthHead;
        }
        var current = _state.Growth.FirstOrDefault(g => g.Planet == FlowDraft.GrowthPlanet);
        var lostWorld = current == null && FlowDraft.GrowthPlanet != null;
        if (lostWorld)
        {
            FlowDraft.GrowthPlanet = null;
            FlowDraft.GrowthFeedback = Loc.GetString("sw-global-war-growth-lost-world");
        }
        var grants = _state.GrowthGrants.Where(g => g.Planet == FlowDraft.GrowthPlanet).ToArray();
        UpdateGrowthPicker(GrowthWorld, ref _growthWorlds, _state.Growth.Select(g => g.Planet).ToArray(),
            FlowDraft.GrowthPlanet, "sw-global-war-growth-select-world", id => PlanetName(id));
        UpdateGrowthPicker(GrowthGrantPicker, ref _growthGrantIds, grants.Select(g => g.Id.ToString()).ToArray(),
            FlowDraft.GrowthGrant.ToString(), _state.CanManageFlows ? "sw-global-war-economy-no-grant" : "sw-global-war-economy-select-grant",
            id => grants.Single(g => g.Id.ToString() == id).Employee);
        UpdateGrowthPicker(GrowthWorker, ref _growthWorkerIds, _state.FlowWorkers.Select(WorkerKey).ToArray(),
            FlowDraft.GrowthWorker is { } worker ? WorkerKey(worker) : null, "sw-global-war-economy-select-worker",
            id => _state.FlowWorkers.Single(w => WorkerKey(w) == id).Name);
        if (FlowDraft.GrowthWorker != null && GrowthWorker.SelectedId < 0)
        {
            FlowDraft.GrowthWorker = null;
            FlowDraft.GrowthFeedback = Loc.GetString("sw-global-war-economy-selection-lost");
        }
        var grant = grants.FirstOrDefault(g => g.Id == FlowDraft.GrowthGrant);
        if (FlowDraft.GrowthGrant != Guid.Empty && grant == null)
        {
            FlowDraft.GrowthGrant = Guid.Empty;
            FlowDraft.GrowthFeedback = Loc.GetString("sw-global-war-economy-selection-lost");
        }
        if (lostWorld)
            FlowDraft.GrowthFeedback = Loc.GetString("sw-global-war-growth-lost-world");
        foreach (var result in _state.GrowthResults)
            if (result.Request == FlowDraft.PendingGrowth?.Request)
                ReceiveGrowthResult(result);
        var pending = FlowDraft.PendingGrowth != null;
        var failure = _state.LogisticsHeadDecisionRequired && !GrowthHead.Pressed ? SWGlobalWarGrowthFailure.HeadDecisionRequired :
            !int.TryParse(GrowthPercent.Text, out var proposed) || proposed is < 0 or > 25 ? SWGlobalWarGrowthFailure.InvalidRequest :
            _state.CanManageFlows ? SWGlobalWarGrowthFailure.None : grant == null ? SWGlobalWarGrowthFailure.Denied :
            grant.Failure != SWGlobalWarGrowthFailure.None ? grant.Failure : proposed < grant.Minimum || proposed > grant.Maximum
                ? SWGlobalWarGrowthFailure.OutsideRange : SWGlobalWarGrowthFailure.None;
        GrowthHead.Visible = _state.Head;
        GrowthDelegation.Visible = _state.CanManageFlows;
        GrowthSave.Disabled = pending || current == null || current.Revision != FlowDraft.GrowthRevision ||
            failure != SWGlobalWarGrowthFailure.None;
        GrowthReapply.Visible = current != null && current.Revision != FlowDraft.GrowthRevision;
        GrowthReapply.Disabled = pending || failure != SWGlobalWarGrowthFailure.None;
        GrowthReapply.Text = Loc.GetString("sw-global-war-growth-reapply", ("percent", GrowthPercent.Text));
        GrowthLoad.Disabled = pending || current == null;
        GrowthWorld.Disabled = pending;
        GrowthRetry.Disabled = !pending;
        GrowthGrant.Disabled = pending || current == null || FlowDraft.GrowthWorker == null;
        GrowthRevoke.Disabled = pending || grant == null || grant.Revoked;
        SetText(GrowthCurrent, current == null ? Loc.GetString("sw-global-war-growth-select-world") :
            Loc.GetString(current.Revision == FlowDraft.GrowthRevision ? "sw-global-war-growth-current" : "sw-global-war-growth-changed",
                ("percent", current.Percent), ("draft", GrowthPercent.Text)));
        SetText(GrowthScope, grant == null ? string.Empty : Loc.GetString("sw-global-war-growth-scope",
            ("minimum", grant.Minimum), ("maximum", grant.Maximum), ("seconds", Math.Max(0, (int) (grant.Expires - _state.Time).TotalSeconds)),
            ("status", GrowthFailureText(grant.Failure))));
        SetText(GrowthReason, current == null ? string.Empty : GrowthFailureText(failure));
        SetText(GrowthResult, FlowDraft.GrowthFeedback);
    }

    private static string GrowthFailureText(SWGlobalWarGrowthFailure failure) => failure == SWGlobalWarGrowthFailure.None
        ? string.Empty : Loc.GetString("sw-global-war-growth-failure-" + failure.ToString().ToLowerInvariant());

    private static void UpdateGrowthPicker(OptionButton picker, ref string[] previous, string[] values, string? selected,
        string emptyKey, Func<string, string> label)
    {
        if (!previous.SequenceEqual(values) || picker.ItemCount == 0)
        {
            previous = values;
            picker.Clear();
            picker.AddItem(Loc.GetString(emptyKey), -1);
            for (var i = 0; i < values.Length; i++)
                picker.AddItem(label(values[i]), i);
        }
        else
        {
            picker.SetItemText(0, Loc.GetString(emptyKey));
            for (var i = 0; i < values.Length; i++)
                picker.SetItemText(i + 1, label(values[i]));
        }
        picker.SelectId(Array.IndexOf(values, selected));
    }
}
