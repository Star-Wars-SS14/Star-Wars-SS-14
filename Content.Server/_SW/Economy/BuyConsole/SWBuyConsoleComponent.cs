using Content.Shared._SW.Economy.Prototypes;
using Content.Shared.Cargo.Prototypes;
using Content.Shared.DeviceLinking;
using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.Economy.BuyConsole;
[RegisterComponent]
public sealed partial class SWBuyConsoleComponent : Component
{
    [DataField("allowed")]
    public List<ProtoId<SWCurrencyPrototype>> AllowedCurrencies = new();

    /// <summary>
    /// Extra products that we need to add except products in AllowedMarkets
    /// </summary>
    [DataField]
    public List<ProtoId<SWCargoProductPrototype>> ExtraProducts = new();

    [DataField]
    public List<ProtoId<CargoMarketPrototype>> AllowedMarkets = new();

    [DataField]
    public ProtoId<SourcePortPrototype> Port = "SWBuyConsoleSender";

    /// <summary>
    /// The most items one order can hold.
    /// </summary>
    [DataField]
    public int MaxOrderAmount = 100;

    public HashSet<ProtoId<SWCargoProductPrototype>> AvailableProducts = new();
}
