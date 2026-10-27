using System.Linq;
using Content.Server._SW._Senate.Economy;
using Content.Server._SW._Senate.Economy.Simulation;
using Content.Shared._SW._Senate.Economy;
using Content.Shared._SW._Senate.Ministries;

namespace Content.Server._SW._Senate.Core;

public sealed partial class SWMinistryHealthSystem
{
    private EconomySnapshot? _materialStockSnapshot;
    private readonly Dictionary<string, double> _materialWarehouseStock = new();

    private List<SWHealthMaterialView> TrialMaterialViews(EconomyProjectState? trial)
    {
        if (trial != null) return trial.Materials.Select(MaterialView).ToList();
        if (_trialProject == null && _stage > SWHealthStage.Trials) return new();
        var pending = _trialProject != null;
        var requirements = pending && _pendingTrialMaterials.Length > 0 ? _pendingTrialMaterials : TrialMaterialRequirements();
        return requirements.Select(material => MaterialView(new EconomyProjectMaterialState(material.Good, material.Quantity, 0, 0)
        {
            DeliveryStatus = pending ? "awaiting_publication" : "awaiting_order", Priority = pending,
        })).ToList();
    }

    private SWHealthMaterialView MaterialView(EconomyProjectMaterialState material)
    {
        var snapshot = _economy.Snapshot;
        if (!ReferenceEquals(snapshot, _materialStockSnapshot))
        {
            _materialStockSnapshot = snapshot;
            _materialWarehouseStock.Clear();
            var republicWorlds = _economy.Definition?.Worlds.Where(w => w.Active).Select(w => w.Id).ToHashSet()
                ?? _worlds.Keys.ToHashSet();
            foreach (var (good, quantity) in SWRepublicMedicalSupplyPublication.WarehouseStock(snapshot, republicWorlds))
                _materialWarehouseStock[good] = quantity;
        }
        return new SWHealthMaterialView
        {
            GoodId = material.Good, StockKnown = snapshot != null, Name = _proto.Index<SWCommodityPrototype>(material.Good).Name,
            Required = material.Required, Delivered = material.Delivered, Incoming = material.Incoming,
            QuantityTolerance = material.QuantityTolerance,
            // Warehouse stock includes stock held for allocation, but excludes installed assets,
            // production WIP, in-flight cargo and private finished vaccine batches.
            RepublicStock = _materialWarehouseStock.GetValueOrDefault(material.Good),
            DeliveryHours = material.DeliveryHours, MinimumTravelHours = material.MinimumTravelHours,
            NextDeliveryHours = material.NextDeliveryHours, NextMinimumTravelHours = material.NextMinimumTravelHours,
            NextDeliveryQuantity = material.NextDeliveryQuantity,
            DeliveryStatus = material.DeliveryStatus, Priority = material.Priority,
        };
    }
}
