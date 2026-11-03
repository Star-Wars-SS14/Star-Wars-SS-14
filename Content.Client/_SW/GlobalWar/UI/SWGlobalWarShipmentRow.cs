using System.Linq;
using System.Numerics;
using Content.Shared._SW.GlobalWar;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client._SW.GlobalWar.UI;

/// <summary>One cargo: what and how much, from where to where, and a chip with the time or the wait.</summary>
internal sealed class SWGlobalWarShipmentRow : PanelContainer
{
    private readonly string _name;
    private readonly Label _cargo = new() { ClipText = true, FontOverride = SWGlobalWarConsoleStyle.Strong, FontColorOverride = Color.White };
    private readonly Label _route = new() { ClipText = true, FontOverride = SWGlobalWarConsoleStyle.Small, FontColorOverride = SWGlobalWarConsoleStyle.TextDim };
    private readonly SWGlobalWarChip _chip = new();
    private readonly SWGlobalWarReturnRowAction _returnAction = new();
    public event Action? ReturnRequested;

    public void SetReturnAvailable(bool visible, bool enabled)
    {
        _returnAction.Visible = visible;
        _returnAction.SetAvailable(enabled);
    }

    public SWGlobalWarShipmentRow(SWGlobalWarCommodityPrototype commodity)
    {
        _name = Loc.GetString(commodity.Name);
        HorizontalExpand = true;
        MouseFilter = MouseFilterMode.Stop;
        PanelOverride = SWGlobalWarConsoleStyle.RowBox(false);
        var body = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, Margin = new Thickness(6, 4), SeparationOverride = 4 };
        var header = new BoxContainer { SeparationOverride = 8, MinHeight = 54 };
        header.AddChild(SWGlobalWarMarketPresentation.CommodityPicture(commodity, new Vector2(48), new Thickness(0)));
        var words = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, VerticalAlignment = VAlignment.Center, SeparationOverride = 2 };
        words.AddChild(_cargo);
        words.AddChild(_route);
        header.AddChild(words);
        header.AddChild(_chip);
        body.AddChild(header);
        body.AddChild(_returnAction);
        _returnAction.Requested += () => ReturnRequested?.Invoke();
        AddChild(body);
    }

    public void Update(SWGlobalWarShipmentView shipment, TimeSpan time, Func<ProtoId<SWGlobalWarPlanetPrototype>, string> name,
        TimeSpan travelTime)
    {
        SetText(_cargo, Loc.GetString("sw-global-war-market-shipment-cargo", ("name", _name), ("count", shipment.Quantity.ToString("0.###"))));
        SetText(_route, $"{name(shipment.Source)} → {name(shipment.Destination)}");
        var leg = Loc.GetString("sw-global-war-market-shipment-leg", ("from", name(shipment.From)), ("to", name(shipment.To)));
        if (shipment.Supply?.PhysicalWorlds is { } worlds)
            leg = Loc.GetString("sw-global-war-supply-physical-worlds", ("worlds", string.Join(", ", worlds.Select(name))));
        if (shipment.Supply is { } supply)
            leg = Loc.GetString(supply.AcceptsSupply ? "sw-global-war-supply-transport-part" :
                "sw-global-war-supply-transport-part-storage", ("leg", leg),
                ("number", supply.Number), ("part", shipment.Id.ToString()[..8]),
                ("accepted", supply.Accepted), ("outstanding", supply.Outstanding), ("recipient", supply.Recipient));

        string detail;
        if (shipment.Supply?.PhysicalWorlds != null)
        {
            detail = Loc.GetString("sw-global-war-supply-physical-unload");
            _chip.Set(Loc.GetString("sw-global-war-market-chip-unload"), "off");
        }
        else if (shipment.ArrivesAt is { } arrival)
        {
            var seconds = (int) Math.Ceiling(Math.Max(0, (arrival - time).TotalSeconds));
            detail = Loc.GetString(shipment.Returning ? "sw-global-war-market-return-arrival" : "sw-global-war-market-shipment-arrival", ("seconds", seconds));
            _chip.Set(Loc.GetString("sw-global-war-market-chip-seconds", ("seconds", seconds)), "good", shipment.Returning ? "returning" : null);
        }
        else
        {
            detail = Loc.GetString(shipment.HoldReason.Length > 0 ? shipment.HoldReason : "sw-global-war-market-shipment-waiting");
            _chip.Set(Loc.GetString("sw-global-war-market-chip-waiting"), "wait", "waiting");
        }

        var proof = Loc.GetString("sw-global-war-market-cargo-proof", ("id", shipment.Id),
            ("origin", Faction(shipment.Origin)), ("owner", Faction(shipment.Owner)),
            ("capture", Loc.GetString(shipment.Captured ? "sw-global-war-market-captured" : "sw-global-war-market-not-captured")),
            ("route", string.Join(" → ", shipment.Route.Select(name))));
        if (shipment.Capture is { } capture)
            proof = Loc.GetString("sw-global-war-market-capture-proof", ("cargo", proof),
                ("operation", capture.Operation), ("transfer", capture.TransferOperation),
                ("node", name(capture.Node)), ("batch", capture.Batch),
                ("quantity", capture.Quantity.ToString("0.###")));
        ToolTip = string.Join("\n", detail, leg, proof);
    }

    private static string Faction(SWGlobalWarFaction? faction) => Loc.GetString(faction switch
    {
        SWGlobalWarFaction.Republic => "sw-global-war-republic",
        SWGlobalWarFaction.Empire => "sw-global-war-empire",
        _ => "sw-global-war-neutral",
    });

    private static void SetText(Label label, string text)
    {
        if (label.Text != text)
            label.Text = text;
    }
}
