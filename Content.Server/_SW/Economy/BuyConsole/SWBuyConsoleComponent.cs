using Content.Shared._SW.Economy;
using Content.Shared.Cargo.Prototypes;
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

    public HashSet<EntityUid> ConnectedPallets = new HashSet<EntityUid>();

    public HashSet<ProtoId<SWCargoProductPrototype>> AvailableProducts = new();
}
