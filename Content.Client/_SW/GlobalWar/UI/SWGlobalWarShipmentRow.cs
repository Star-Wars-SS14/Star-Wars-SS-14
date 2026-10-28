using System.Linq;
using System.Numerics;
using Content.Shared._SW.GlobalWar;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client._SW.GlobalWar.UI;

internal sealed class SWGlobalWarShipmentRow : PanelContainer
{
    private readonly Label _quantity = new() { FontOverride = SWGlobalWarConsoleStyle.Strong };
    private readonly RichTextLabel _route = new();
    private readonly RichTextLabel _leg = new();
    private readonly Label _status = new() { FontOverride = SWGlobalWarConsoleStyle.Small };
    private readonly SWGlobalWarMarketBar _bar = new();
    private string? _lastRoute;
    private string? _lastLeg;

    public SWGlobalWarShipmentRow(SWGlobalWarCommodityPrototype commodity)
    {
        HorizontalExpand = true;
        PanelOverride = new StyleBoxFlat { BackgroundColor = SWGlobalWarConsoleStyle.Navigation };
        var body = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, Margin = new Thickness(12), SeparationOverride = 6 };
        var header = new BoxContainer { SeparationOverride = 8 };
        var icon = new EntityPrototypeView { SetSize = new Vector2(28), Stretch = SpriteView.StretchMode.Fill };
        icon.SetPrototype(commodity.Entity);
        header.AddChild(icon);
        header.AddChild(new Label
        {
            Text = Loc.GetString(commodity.Name), ClipText = true, HorizontalExpand = true,
            FontColorOverride = SWGlobalWarMarketPresentation.CommodityColor(commodity.ID),
            FontOverride = SWGlobalWarConsoleStyle.Strong,
        });
        header.AddChild(_quantity);
        body.AddChild(header);
        body.AddChild(_route);
        body.AddChild(_leg);
        body.AddChild(_status);
        body.AddChild(_bar);
        AddChild(body);
    }

    public void Update(SWGlobalWarShipmentView shipment, TimeSpan time, Func<ProtoId<SWGlobalWarPlanetPrototype>, string> name,
        TimeSpan travelTime)
    {
        SetText(_quantity, $"×{shipment.Quantity}");
        var route = $"{name(shipment.Source)} → {name(shipment.Destination)}";
        var leg = Loc.GetString("sw-global-war-market-shipment-leg", ("from", name(shipment.From)), ("to", name(shipment.To)));
        if (_lastRoute != route)
        {
            _lastRoute = route;
            _route.SetMessage(route);
        }
        if (_lastLeg != leg)
        {
            _lastLeg = leg;
            _leg.SetMessage(leg);
        }
        if (shipment.ArrivesAt is { } arrival)
        {
            SetText(_status, Loc.GetString("sw-global-war-market-shipment-arrival",
                ("seconds", (int) Math.Ceiling(Math.Max(0, (arrival - time).TotalSeconds)))));
            if (_status.FontColorOverride != SWGlobalWarConsoleStyle.Accent)
                _status.FontColorOverride = SWGlobalWarConsoleStyle.Accent;
            _bar.Visible = true;
            _bar.Value = travelTime.TotalSeconds - (arrival - time).TotalSeconds;
            _bar.Maximum = travelTime.TotalSeconds;
        }
        else
        {
            SetText(_status, Loc.GetString("sw-global-war-market-shipment-waiting"));
            if (_status.FontColorOverride != SWGlobalWarConsoleStyle.Brass)
                _status.FontColorOverride = SWGlobalWarConsoleStyle.Brass;
            _bar.Visible = false;
        }
        ToolTip = string.Join(" → ", shipment.Route.Select(name));
    }

    private static void SetText(Label label, string text)
    {
        if (label.Text != text)
            label.Text = text;
    }
}
