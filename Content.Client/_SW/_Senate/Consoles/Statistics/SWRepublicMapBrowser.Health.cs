using System;
using System.Linq;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Economy;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._SW._Senate.Consoles.Statistics;

public sealed partial class SWRepublicMapBrowser
{
    private bool _showMedicalSupply;
    private string? _selectedMedicalShipmentId;
    private readonly Button _medicalToggle = SWConsoleStyle.TabButton("Медицинские поставки");
    public void SetMedicalSupplyVisible(bool visible)
    {
        _showMedicalSupply = visible;
        Map.SetMedicalSupplyVisible(visible);
        SWConsoleStyle.StyleTabButton(_medicalToggle, visible);
    }
    private void SelectMedicalShipment(SWHealthSupplyFlowView flow)
    {
        _selectedMedicalShipmentId = flow.Id;
        _detailHeading.RemoveAllChildren();
        _detail.RemoveAllChildren();
        _detailHeading.AddChild(SWConsoleStyle.Wrap("Медицинская поставка", SWConsoleStyle.Good));
        _detail.AddChild(SWConsoleStyle.Wrap($"{SWGalacticMapControl.MedicalKindName(flow.Kind, flow.Payload)}{SWGalacticMapControl.MedicalStrainName(flow.StrainId)}\n" +
            $"Откуда: {WorldName(flow.From)}\nКуда: {WorldName(flow.To)}\n" +
            $"{flow.GoodName}: {flow.Quantity:0.###}\nПолучатель: {WorldName(flow.TargetWorldId)}\n" +
            SWGalacticMapControl.MedicalDeliveryEta(flow, _economyView?.Hour ?? 0, _economyView?.Paused ?? false), SWConsoleStyle.TextBright));
        string WorldName(string id) => _worlds.TryGetValue(id, out var world) ? world.Name : "Неизвестный мир";
    }
    private bool RefreshMedicalShipment()
    {
        if (_selectedMedicalShipmentId == null) return false;
        var flow = _economyView?.MedicalSupplyFlows.FirstOrDefault(f => f.Id == _selectedMedicalShipmentId);
        if (flow == null) { _selectedMedicalShipmentId = null; return false; }
        SelectMedicalShipment(flow);
        return true;
    }
}
