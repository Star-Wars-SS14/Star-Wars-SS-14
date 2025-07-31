using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.TransferSystem;
[RegisterComponent]
public sealed partial class SWTransferPalletComponent : Component
{
    [DataField]
    public string OwnerFaction = "";
}
