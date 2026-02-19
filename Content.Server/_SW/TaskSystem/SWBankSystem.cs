using Content.Server._SW.TaskSystem;
using Content.Shared._SW.Economy;
using Content.Shared._SW.Economy.BankSystem;
using Content.Shared._SW.Economy.Prototypes;
using Content.Shared._SW.TaskSystem;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Stacks;
using Content.Shared.Store;
using Robust.Shared.Prototypes;
using Robust.Shared.Toolshed.TypeParsers;
using Robust.Shared.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.Economy.BankSystem;

public sealed partial class SWBankSystem : SWSharedBankSystem
{
    [Dependency] private readonly SWFactionGameruleSystem _factionSystem = default!;
    public bool CanGetFactionAccount(string faction)
    {
        var factEnt = _factionSystem.GetFactionEntity(faction);
        if (factEnt == null)
        {
            return false;
        }
        if (!TryComp<SWBankAccountContainerComponent>(factEnt, out var comp))
        {
            return false;
        }
        return true;
    }

    public Entity<SWBankAccountContainerComponent> GetFactionAccount(string faction)
    {
        var factEnt = _factionSystem.GetFactionEntity(faction);
        DebugTools.Assert(factEnt != null);
        var comp = Comp<SWBankAccountContainerComponent>(factEnt.Value.Owner);

        return new Entity<SWBankAccountContainerComponent>(factEnt.Value, comp);
    }

    public Entity<SWBankAccountContainerComponent>? TryGetFactionAccount(string faction)
    {
        if (CanGetFactionAccount(faction))
        {
            return GetFactionAccount(faction);
        }
        return null;
    }

    public void UpdateAllInterfaces()
    {
        var query = EntityQueryEnumerator<SWBankAccountAccessorComponent>();
        while(query.MoveNext(out var _, out var comp))
        {
            comp.AccountInterfaceRequiresUpdate = true;
        }
    }
}
