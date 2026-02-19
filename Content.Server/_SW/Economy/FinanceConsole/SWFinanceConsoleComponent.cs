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

    /// <summary>
    /// In some cases we dont want this component to update ui, because we do it by other components
    /// </summary>
    [DataField]
    public bool UpdateUI = true;
}
