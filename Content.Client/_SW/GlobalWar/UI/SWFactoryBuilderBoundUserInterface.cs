using System.Linq;
using System.Numerics;
using Content.Shared._SW.GlobalWar;
using Content.Shared.RCD;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._SW.GlobalWar.UI;

public sealed class SWFactoryBuilderBoundUserInterface(EntityUid owner, Enum uiKey) : SWGlobalWarBoundUserInterface(owner, uiKey)
{
    private SWFactoryBuilderMenu? _menu;
    private SWFactoryBuilderSnapshot? _snapshot;
    protected override void Open()
    {
        base.Open();
        _menu = this.CreateWindow<SWFactoryBuilderMenu>();
        _menu.Selected += id =>
        {
            SendPredictedMessage(new RCDSystemMessage(id));
            SendPredictedMessage(new SWFactoryBuilderRefresh());
        };
        if (_snapshot != null)
            _menu.Update(_snapshot);
        SendPredictedMessage(new SWFactoryBuilderRefresh());
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);
        if (message is not SWFactoryBuilderSnapshot snapshot)
            return;
        _snapshot = snapshot;
        if (IsOpened)
            _menu?.Update(snapshot);
    }
}

public sealed class SWFactoryBuilderMenu : SWGlobalWarWindow
{
    public event Action<string>? Selected;
    private readonly OptionButton _picker = new() { HorizontalExpand = true };
    private readonly RichTextLabel _status = new();
    private readonly RichTextLabel _help = new();
    private readonly Label _budget = new();
    private readonly Label _price = new();
    private readonly BoxContainer _costs = new() { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 6 };
    private readonly EntityPrototypeView _preview = new() { SetSize = new Vector2(96, 96) };
    private List<SWFactoryBuilderOption> _options = new();

    public SWFactoryBuilderMenu()
    {
        SetCampaignTitle(Loc.GetString("sw-builder-title"));
        SetSize = new Vector2(560, 540);
        MinSize = new Vector2(480, 400);
        var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 10, Margin = new Thickness(12), HorizontalExpand = true, VerticalExpand = true };
        root.AddChild(_status);
        root.AddChild(_picker);
        root.AddChild(_preview);
        root.AddChild(_budget);
        root.AddChild(_price);
        var scroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false };
        scroll.AddChild(_costs);
        root.AddChild(scroll);
        root.AddChild(_help);
        ContentsContainer.AddChild(root);
        _status.SetMessage(Loc.GetString("sw-builder-loading"));
        _picker.Disabled = true;
        _preview.Visible = false;
        _picker.OnItemSelected += args =>
        {
            _picker.SelectId(args.Id);
            Selected?.Invoke(args.Id == 0 ? "SWFactoryDeconstruct" : _options[args.Id - 1].Id);
        };
    }

    public void Update(SWFactoryBuilderSnapshot state)
    {
        _picker.Disabled = false;
        if (!_options.Select(o => o.Id).SequenceEqual(state.Options.Select(o => o.Id)) || _picker.ItemCount == 0)
        {
            _picker.Clear();
            _picker.AddItem(Loc.GetString("sw-builder-dismantle"), 0);
            for (var i = 0; i < state.Options.Count; i++)
                _picker.AddItem(state.Options[i].Name, i + 1);
        }
        _options = state.Options;
        var selected = _options.FindIndex(o => o.Id == state.Selected);
        _picker.SelectId(selected + 1);
        _status.SetMessage(state.Status);
        _budget.Text = Loc.GetString("sw-builder-budget-value", ("credits", state.Budget));
        _costs.DisposeAllChildren();
        _preview.Visible = selected >= 0;
        if (selected < 0)
        {
            _price.Text = Loc.GetString("sw-builder-refund");
            _help.SetMessage(Loc.GetString("sw-builder-dismantle-help"));
            return;
        }
        var option = _options[selected];
        _preview.SetPrototype(option.Entity);
        _price.Text = Loc.GetString("sw-builder-price", ("credits", option.Price));
        _price.FontColorOverride = state.Budget >= option.Price && state.Allowed ? Color.White : Color.Red;
        foreach (var cost in option.Costs)
        {
            _costs.AddChild(new Label
            {
                Text = Loc.GetString("sw-builder-cost", ("name", cost.Name), ("need", cost.Need), ("have", cost.Have)),
                FontColorOverride = cost.Have >= cost.Need && state.Allowed ? Color.White : Color.Red,
            });
        }
        _help.SetMessage(Loc.GetString("sw-builder-build-help"));
    }
}
