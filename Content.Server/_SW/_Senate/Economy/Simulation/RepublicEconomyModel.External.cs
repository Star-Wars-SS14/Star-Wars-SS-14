using System;
using System.Collections.Generic;

namespace Content.Server._SW._Senate.Economy.Simulation;

public sealed partial class RepublicEconomyModel
{
    private readonly Dictionary<string, EconomyExternalTransferResult> _externalResults = new();

    /// <summary>Executed exclusively by the economy worker; duplicate IDs return the original receipt.</summary>
    public EconomyExternalTransferResult ApplyExternalTransfer(EconomyExternalTransfer command)
    {
        if (_externalResults.TryGetValue(command.Id, out var previous)) return previous;
        var quantity = 0d;
        var key = new Key(command.World, command.Good);
        if (double.IsFinite(command.Amount) && command.Amount > 0 && _stores.TryGetValue(key, out var store)
            && _worlds.TryGetValue(command.World, out var world) && world.Active)
        {
            if (command.Deposit)
            {
                quantity = Math.Min(command.Amount, Math.Max(0, StorageCapacity(key) - store.Quantity - Get(Reservations(), key)));
                store.Quantity += quantity; _ledgers[key.Good].ExternalIn += quantity;
            }
            else
            {
                quantity = Math.Min(command.Amount, Math.Max(0, store.Quantity));
                store.Quantity -= quantity; _ledgers[key.Good].ExternalOut += quantity;
            }
        }
        var result = new EconomyExternalTransferResult(command.Id, quantity);
        _externalResults[command.Id] = result;
        return result;
    }
}
