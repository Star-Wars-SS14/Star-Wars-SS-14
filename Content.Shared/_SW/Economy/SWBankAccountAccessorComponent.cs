using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW.Economy;
[RegisterComponent]
public sealed partial class SWBankAccountAccessorComponent : Component
{
    public Entity<SWBankAccountContainerComponent>? Account = null;

    public bool AccountInterfaceRequiresUpdate = false;

    [DataField(required: true)]
    public Enum UiKey = SWBasePayableConsoleUIKey.Key;
}
