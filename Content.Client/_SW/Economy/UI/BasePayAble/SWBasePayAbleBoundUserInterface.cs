using Content.Shared._SW.Economy;
using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Robust.Client.UserInterface.Controls.MenuBar;

namespace Content.Client._SW.Economy.UI.BasePayAble;
public abstract partial class SWBasePayAbleBoundUserInterface : BoundUserInterface
{
    public SWBasePayAbleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    public abstract void PopulateBalance(Dictionary<ProtoId<SWCurrencyPrototype>, float> amount);
    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        ISawmill _s = Logger.GetSawmill("sadf");
        _s.Fatal("dsfg");
        switch (state)
        {
            case SWBasePayableBounderUserInterfaceState msg:
                if (msg.CurrentAmount.Keys.Count != 0)
                {
                    PopulateBalance(msg.CurrentAmount);
                }
                _s.Fatal("done");
                break;
        }
    }
}
