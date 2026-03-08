using Content.Shared._SW.Economy.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW.Economy.BankSystem;

[RegisterComponent, NetworkedComponent]
public sealed partial class SWBankAccountContainerComponent : Component
{
    [DataField]
    public Dictionary<ProtoId<SWCurrencyPrototype>, float> CurrentAmount = new Dictionary<ProtoId<SWCurrencyPrototype>, float>();

    [DataField]
    public List<ProtoId<SWCurrencyPrototype>> AllowedCurrencies = new List<ProtoId<SWCurrencyPrototype>>() {
        "SWRepublicCreditCurrency",
        "SWEmpireCreditCurrency",
        "SWReinforcementScore",
        "SWSupplyScoresRepublic",
        "SWSupplyScoresEmpire",
        "SWMandalorianSupplyScores",
        "SWBlackMarketSupplyScores"};
}
