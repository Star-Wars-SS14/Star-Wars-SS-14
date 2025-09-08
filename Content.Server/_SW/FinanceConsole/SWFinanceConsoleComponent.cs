using Content.Server._SW.Economy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.FinanceConsole;
[RegisterComponent]
public sealed partial class SWFinanceConsoleComponent : Component
{
    [DataField(required: true)]
    public string Faction = "";

    public Entity<SWBankAccountContainerComponent>? Account = null;
}
