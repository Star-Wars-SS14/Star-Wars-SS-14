using Content.Shared._SW.Economy.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW.Economy.Messages;

[NetSerializable, Serializable]
public enum SWSellConsoleUIKey
{
    Key,
}

[NetSerializable, Serializable]
public sealed class SWCargoPalletConsoleInterfaceState : BoundUserInterfaceState
{
    /// <summary>
    /// estimated apraised value of all the entities on top of pallets on the same grid as the console
    /// </summary>
    public int Appraisal;

    /// <summary>
    /// number of entities on top of pallets on the same grid as the console
    /// </summary>
    public int Count;

    /// <summary>
    /// are the buttons enabled
    /// </summary>
    public bool Enabled;

    public ProtoId<SWCurrencyPrototype> OutputCurId;

    public SWCargoPalletConsoleInterfaceState(int appraisal, int count, bool enabled, ProtoId<SWCurrencyPrototype> id)
    {
        Appraisal = appraisal;
        Count = count;
        Enabled = enabled;
        OutputCurId = id;
    }
}

