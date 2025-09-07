using Robust.Shared.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW.Economy;

[NetSerializable, Serializable]
public enum SWFinanceConsoleUIKey
{
    Key,
}

[Serializable, NetSerializable]
public sealed class SWFinanceConsoleBounderUserInterfaceState : BoundUserInterfaceState
{
    public SWFinanceConsoleBounderUserInterfaceState()
    {
    }
}
