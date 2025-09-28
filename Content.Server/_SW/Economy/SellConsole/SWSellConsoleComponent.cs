using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Content.Server.Cargo.Components;
using Content.Shared.DeviceLinking;
using Content.Shared._SW.Economy.Prototypes;

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
    public ProtoId<SWCurrencyPrototype> StaticPriceCurrency = "SWVanillaCurrency";

    [DataField]
    public ProtoId<SourcePortPrototype> Port = "SWSellConsoleSender";
}
