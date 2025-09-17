using Content.Shared._SW.Economy;
using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Content.Server.Cargo.Components;

namespace Content.Server._SW.Economy.SellConsole;

[RegisterComponent]
public sealed partial class SWSellConsoleComponent : Component
{
    [DataField(required: true)]
    public ProtoId<SWCurrencyPrototype> OutputCurrency;

    /// <summary>
    /// Price in <see cref="StaticPriceComponent">
    /// will be considered as price in this currency
    /// </summary>
    [DataField]
    public ProtoId<SWCurrencyPrototype> StaticPriceCurrency;

    public List<EntityUid> LinkedPallets = new List<EntityUid>();
}
